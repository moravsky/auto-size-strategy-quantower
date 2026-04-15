using System;
using TradingPlatform.BusinessLayer;

namespace AutoSizeStrategy
{
    public class StrategyEngine(IStrategyContext context) : IDisposable
    {
        private readonly TrackingSet<long> _processedRequests = new();

        public bool ProcessRequest(RequestParameters requestParameters)
        {
            var wrapper = RequestParametersWrapper.Create(requestParameters);
            return ProcessRequest(wrapper);
        }

        public virtual bool ProcessRequest(IRequestParameters requestParameters)
        {
            if (requestParameters is not IOrderRequestParameters orderRequestParameters)
                return false;

            context.Logger.LogVerbose(
                $"ProcessRequest entry: type={requestParameters.GetType().Name} reqId={orderRequestParameters.RequestId} " +
                $"symbol={orderRequestParameters.Symbol.Name} side={orderRequestParameters.Side}"
            );

            if (!PerformInitialValidations(orderRequestParameters))
                return false;

            double netPosition = context.TradingService.GetNetPositionQuantity(
                orderRequestParameters.Account,
                orderRequestParameters.Symbol
            );

            if (orderRequestParameters.IsExitForPosition(netPosition))
            {
                context.Logger.LogVerbose(
                    $"Passing through opposite side request {orderRequestParameters.RequestId}. NetPos: {netPosition}"
                );
                return false;
            }

            bool hasStopLoss = orderRequestParameters.StopLossItems?.Count > 0;
            if (!hasStopLoss && context.Settings.MissingStopLossAction == MissingStopLossAction.Reject)
            {
                context.Logger.LogInfo($"Order request {orderRequestParameters.RequestId} cancelled: stop loss required");
                return true;
            }

            int targetQuantity = DetermineTargetQuantity(orderRequestParameters, netPosition);

            if (targetQuantity <= 0)
            {
                HandleZeroQuantity(requestParameters, orderRequestParameters);
                return true;
            }

            return ExecuteOrderUpdate(requestParameters, orderRequestParameters, targetQuantity);
        }

        private bool PerformInitialValidations(IOrderRequestParameters orderRequestParameters)
        {
            if (context.Settings.CurrentAccount == null)
            {
                context.Logger.LogError("Target account not set, cannot continue");
                return false;
            }

            if (
                context.Settings.DrawdownMode == DrawdownMode.EndOfDay
                && context.Settings.MinAccountBalanceOverride == 0
            )
            {
                context.Logger.LogError(
                    "End of day drawdown accounts require Minimum Balance Override"
                );
                return false;
            }

            if (context.Settings.CurrentAccount.Id != orderRequestParameters.Account.Id)
                return false;

            // Idempotency check
            if (!_processedRequests.TryTrack(orderRequestParameters.RequestId))
            {
                context.Logger.LogVerbose(
                    $"Order request {orderRequestParameters.RequestId} has already been processed - passing through unchanged"
                );
                return false;
            }

            return true;
        }

        private int DetermineTargetQuantity(IOrderRequestParameters orderRequestParameters, double netPosition)
        {
            int calculatedSize;
            bool hasStopLoss = orderRequestParameters.StopLossItems != null && orderRequestParameters.StopLossItems.Count > 0;

            if (!hasStopLoss && context.Settings.MissingStopLossAction == MissingStopLossAction.Ignore)
            {
                // Bypass just the risk math, but still subject this to Caps and Position limits!
                context.Logger.LogInfo($"Order request {orderRequestParameters.RequestId} has no stop loss - bypassing risk math");
                calculatedSize = (int)orderRequestParameters.Quantity;
            }
            else if (hasStopLoss)
            {
                calculatedSize = CalculateRiskBasedSize(orderRequestParameters);
                if (calculatedSize == 0)
                    return 0;
            }
            else
            {
                return 0;
            }

            calculatedSize = ApplyMaxContractsCap(calculatedSize, orderRequestParameters.Symbol);

            // Adjust for current net position
            bool isReversal = orderRequestParameters.Side.IsExitDirection(netPosition);
            int remainingCapacity = isReversal
                ? (int)Math.Abs(netPosition) + calculatedSize
                : calculatedSize - (int)Math.Abs(netPosition);

            context.Logger.LogVerbose(
                $"Position adjustment: netPos={netPosition} calculatedSize={calculatedSize} " +
                $"isReversal={isReversal} remainingCapacity={remainingCapacity}"
            );

            if (remainingCapacity <= 0)
            {
                context.Logger.LogInfo($"Request {orderRequestParameters.RequestId} cancelled: position already at or above target size ({calculatedSize})");
                return 0;
            }

            return remainingCapacity;
        }

        private int CalculateRiskBasedSize(IOrderRequestParameters orderRequestParameters)
        {
            DrawdownMode drawdownMode = context.Settings.DrawdownMode;
            var account = orderRequestParameters.Account;

            double positionRisk = RiskCalculator.CalculatePositionRisk(
                account,
                context.Settings.RiskPercent,
                drawdownMode,
                out var calculationReason,
                minAccountBalanceOverride: context.Settings.MinAccountBalanceOverride
            );

            if (positionRisk <= 0)
            {
                context.Logger.LogInfo($"Risk=0 for {account.Id}. Reason: {calculationReason}");
                return 0;
            }

            var riskCapital = RiskCalculator.GetAvailableRiskCapital(
                account,
                context.Settings.DrawdownMode,
                out _,
                context.Settings.MinAccountBalanceOverride
            );

            context.Logger.LogInfo(
                $"Account balance:{account.Balance} risk capital: {riskCapital} position risk:{positionRisk}"
            );

            var symbol = orderRequestParameters.Symbol;
            double entryPrice = orderRequestParameters.GetLikelyFillPrice();
            double tickSize = symbol.TickSize;
            double tickValue = symbol.GetTickCost(entryPrice);

            if (!double.IsFinite(tickValue) || tickValue <= 0)
            {
                context.Logger.LogError(
                    $"Symbol {symbol.Name} tick value unavailable ({tickValue}), cancelling request {orderRequestParameters.RequestId}"
                );
                return 0;
            }

            var slTpHolder = orderRequestParameters.StopLossItems[0];
            double stopDistanceTicks = RiskCalculator.GetStopDistanceTicks(
                slTpHolder,
                tickSize,
                entryPrice
            );

            context.Metrics.LastSymbol = symbol;
            context.Metrics.LastStopDistanceTicks = stopDistanceTicks;

            double roundTripCommission = context.Settings.GetCommission(symbol) * 2;
            double slippageTicks = context.Settings.AverageSlippageTicks;

            double costPerContract = RiskCalculator.CalculateCostPerContract(
                stopDistanceTicks,
                tickValue,
                slippageTicks,
                roundTripCommission
            );

            context.Logger.LogInfo(
                $"[{symbol.Name}] cost per contract: {costPerContract:F2}, ({stopDistanceTicks}T stop + {slippageTicks}T slip) * {tickValue} tickValue + ${roundTripCommission:F2} comm"
            );

            return RiskCalculator.CalculatePositionSize(
                positionRisk,
                costPerContract
            );
        }

        private void HandleZeroQuantity(IRequestParameters requestParameters, IOrderRequestParameters orderRequestParameters)
        {
            string logMessage = "Insufficient risk budget for this stop loss distance";

            // If this happened because they modified an existing order's SL to be too wide,
            // we must aggressively cancel the working order to protect them.
            if (requestParameters is IModifyOrderRequestParameters modifyZero)
            {
                logMessage +=
                    $". Request {modifyZero.RequestId}: SL too wide, cancelling order {modifyZero.OrderId}";
                context.TradingService.Cancel(modifyZero.OrderId);
            }

            context.Logger.LogInfo(logMessage);
        }

        private int ApplyMaxContractsCap(int calculatedSize, ISymbol symbol)
        {
            int sizeCap = symbol.IsMicro()
                ? context.Settings.MaxContractsMicro
                : context.Settings.MaxContractsMini;

            if (sizeCap > 0 && calculatedSize > sizeCap)
            {
                context.Logger.LogInfo($"Capping calculatedSize from {calculatedSize} to {sizeCap} (Max Contracts limit)");
                return sizeCap;
            }

            return calculatedSize;
        }

        private bool ExecuteOrderUpdate(IRequestParameters requestParameters, IOrderRequestParameters orderRequestParameters, int finalQuantity)
        {
            if (requestParameters is IModifyOrderRequestParameters modifyOrderRequestParameters)
            {
                bool quantityChanged = !MathUtil.Equals(orderRequestParameters.Quantity, finalQuantity);

                if (quantityChanged)
                {
                    context.Logger.LogInfo(
                        $"Request {modifyOrderRequestParameters.RequestId} resizing order " +
                        $"{modifyOrderRequestParameters.OrderId} from {orderRequestParameters.Quantity} " +
                        $"to {finalQuantity} via Cancel/Replace."
                    );
                    var replacementParams = IPlaceOrderRequestParameters.FromModify(modifyOrderRequestParameters, finalQuantity);
                    context.Logger.LogVerbose(
                        $"Replacement reqId={replacementParams.RequestId} for order {modifyOrderRequestParameters.OrderId}"
                    );
                    // Mark as processed so ProcessRequest dosen't re-intercept
                    _processedRequests.TryTrack(replacementParams.RequestId);
                    context.TradingService.CancelReplace(
                        modifyOrderRequestParameters.OrderId,
                        replacementParams
                    );
                    return true;
                }

                // Quantity unchanged — no Cancel/Replace needed, let Quantower route natively.
                context.Logger.LogInfo(
                    $"Request {modifyOrderRequestParameters.RequestId} quantity unchanged " +
                    $"at {finalQuantity} for order {modifyOrderRequestParameters.OrderId}. " +
                    $"SL: [{string.Join(", ", modifyOrderRequestParameters.StopLossItems)}] " +
                    $"TP: [{string.Join(", ", modifyOrderRequestParameters.TakeProfitItems)}]"
                );
                return false;
            }

            if (!MathUtil.Equals(orderRequestParameters.Quantity, finalQuantity))
            {
                context.Logger.LogInfo(
                    $"Changed request {orderRequestParameters.RequestId} quantity from {orderRequestParameters.Quantity} to {finalQuantity}."
                );
                orderRequestParameters.Quantity = finalQuantity;
            }
            return false;
        }
        public void ReportCompletedRequest(RequestParameters requestParameters, object requestResult)
        {
            if (requestParameters is PlaceOrderRequestParameters placeOrderRequestParameters
                && requestResult is TradingOperationResult { Status: TradingOperationResultStatus.Success })
            {
                context.TradingService.ReportPlacedOrder(placeOrderRequestParameters.RequestId);
            }
        }

        public void ReportCancelledOrder(string orderId)
        {
            context.TradingService.ReportCancelledOrder(orderId);
        }

        public void Dispose()
        {
            context.Dispose();
            _processedRequests.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
