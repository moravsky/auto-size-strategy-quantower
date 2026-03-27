using System;
using TradingPlatform.BusinessLayer;

namespace AutoSizeStrategy
{
    public class StrategyEngine(IStrategyContext context) : IDisposable
    {
        private readonly TrackingSet<long> _processedRequests = new();

        public void ProcessRequest(RequestParameters requestParameters)
        {
            var wrapper = RequestParametersWrapper.Create(requestParameters);
            ProcessRequest(wrapper);
        }

        public virtual void ProcessRequest(IRequestParameters requestParameters)
        {
            if (requestParameters is not IOrderRequestParameters orderRequestParameters)
                return;

            if (context.Settings.CurrentAccount == null)
            {
                context.Logger.LogError("Target account not set, cannot continue");
                return;
            }

            if (
                context.Settings.DrawdownMode == DrawdownMode.EndOfDay
                && context.Settings.MinAccountBalanceOverride == 0
            )
            {
                context.Logger.LogError(
                    "End of day drawdown accounts require Minimum Balance Override"
                );
                return;
            }

            if (context.Settings.CurrentAccount.Id != orderRequestParameters.Account.Id)
                return;

            // Idempotency check
            if (!_processedRequests.TryTrack(orderRequestParameters.RequestId))
            {
                context.Logger.LogInfo(
                    $"Order request {orderRequestParameters.RequestId} has already been processed - passing through unchanged"
                );
                return;
            }

            double netPosition = context.TradingService.GetNetPositionQuantity(
                orderRequestParameters.Account,
                orderRequestParameters.Symbol
            );

            if (orderRequestParameters.IsExitForPosition(netPosition))
            {
                context.Logger.LogInfo(
                    $"Passing through exit request {orderRequestParameters.RequestId} (NetPos: {netPosition}) unchanged."
                );
                return;
            }

            if (
                orderRequestParameters.StopLossItems == null
                || orderRequestParameters.StopLossItems.Count == 0
            )
            {
                if (context.Settings.MissingStopLossAction == MissingStopLossAction.Reject)
                {
                    context.Logger.LogInfo(
                        $"Order request {orderRequestParameters.RequestId} cancelled: stop loss required"
                    );
                    orderRequestParameters.Quantity = 0;
                }
                else if (context.Settings.MissingStopLossAction == MissingStopLossAction.Ignore)
                {
                    context.Logger.LogInfo(
                        $"Order request {orderRequestParameters.RequestId} has no stop loss - passing through unchanged"
                    );
                }

                return;
            }

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
                orderRequestParameters.Quantity = 0;
                return;
            }

            var riskCapital = RiskCalculator.GetAvailableRiskCapital(
                account,
                context.Settings.DrawdownMode,
                out _,
                context.Settings.MinAccountBalanceOverride
            );
            context.Logger.LogInfo(
                $"Account balance:{account.Balance} riskCapital: {riskCapital} position risk:{positionRisk}"
            );

            var symbol = orderRequestParameters.Symbol;
            double entryPrice = orderRequestParameters.GetLikelyFillPrice();
            double tickSize = symbol.TickSize;
            double tickValue = symbol.GetTickCost(entryPrice);
            if (!double.IsFinite(tickValue) || tickValue <= 0)
            {
                context.Logger.LogError(
                    $"Symbol {symbol.Id} tick value unavailable ({tickValue}), cancelling request {orderRequestParameters.RequestId}"
                );
                orderRequestParameters.Quantity = 0;
                return;
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

            int calculatedSize = RiskCalculator.CalculatePositionSize(
                positionRisk,
                costPerContract
            );

            // Apply max contracts cap (0 = disabled)
            int sizeCap = symbol.IsMicro()
                ? context.Settings.MaxContractsMicro
                : context.Settings.MaxContractsMini;
            if (sizeCap > 0 && calculatedSize > sizeCap)
            {
                context.Logger.LogInfo(
                    $"Capping calculatedSize from {calculatedSize} to {sizeCap} (Max Contracts limit)"
                );
                calculatedSize = sizeCap;
            }

            if (calculatedSize == 0)
            {
                string logMessage = "Risk too big even for 1 contract";

                if (requestParameters is IModifyOrderRequestParameters modifyZero)
                {
                    logMessage +=
                        $". Request {modifyZero.RequestId}: SL too wide, cancelling order {modifyZero.OrderId}";
                    context.TradingService.Cancel(modifyZero.OrderId);
                }

                context.Logger.LogInfo(logMessage);

                orderRequestParameters.Quantity = 0;
                return;
            }

            int remainingCapacity;
            // If the order is in the opposite direction of our current position (a reversal/exit)
            if (orderRequestParameters.Side.IsExitDirection(netPosition))
            {
                // Capacity = The amount needed to flatten + the max allowed risk in the new direction
                remainingCapacity = (int)Math.Abs(netPosition) + calculatedSize;
            }
            else
            {
                // Pyramiding: Capacity = Max allowed risk - what we already hold
                remainingCapacity = calculatedSize - (int)Math.Abs(netPosition);
            }

            if (remainingCapacity <= 0)
            {
                context.Logger.LogInfo(
                    $"Request {orderRequestParameters.RequestId} cancelled: position already at target size ({calculatedSize})"
                );
                orderRequestParameters.Quantity = 0;
                return;
            }

            if (requestParameters is IModifyOrderRequestParameters modifyOrderRequestParameters)
            {
                context.Logger.LogInfo(
                    $"Request {modifyOrderRequestParameters.RequestId} resizing order {modifyOrderRequestParameters.OrderId}"
                    + $" from {orderRequestParameters.Quantity} to {remainingCapacity} via Cancel/Replace. Total capacity: {calculatedSize}."
                );
                context.TradingService.CancelReplace(
                    modifyOrderRequestParameters.OrderId,
                    IPlaceOrderRequestParameters.FromModify(modifyOrderRequestParameters, remainingCapacity)
                );

                //  CancelReplace is going to cancel current buy/sell order and create new one.
                //  We should cancel the modify order, becuase it's job is already done.
                orderRequestParameters.Quantity = 0;
                return;
            }

            context.Logger.LogInfo(
                $"Changed request {orderRequestParameters.RequestId} quantity from {orderRequestParameters.Quantity} to {remainingCapacity}. Total capacity: {calculatedSize}."
            );
            orderRequestParameters.Quantity = remainingCapacity;
        }

        public void ReportCompletedRequest(RequestParameters requestParameters)
        {
            if (requestParameters is PlaceOrderRequestParameters placeOrderRequestParameters)
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
