using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TradingPlatform.BusinessLayer;

namespace AutoSizeStrategy
{
    // TODO: Run StrategyEngine on a backgroud thread to unblock UI during debugging
    public partial class StrategyEngine(IStrategyContext context) : IDisposable
    {
        private readonly TrackingSet<long> _processedRequests = new();

        public void ProcessRequest(RequestParameters requestParameters)
        {
            // Delegate the "selection logic" to our Factory
            var wrapper = RequestParametersWrapper.Create(requestParameters);

            // Pass the result to the main logic
            ProcessRequest(wrapper);
        }

        public virtual void ProcessRequest(IRequestParameters requestParameters)
        {
            if (requestParameters is not IOrderRequestParameters orderRequestParameters)
                return;

            if (context.Settings.CurrentAccount == null)
            {
                context.Logger.LogError($"Target account not set, cannot continue");
                return;
            }

            if (
                context.Settings.CurrentAccount.InferDrawdownMode() == DrawdownMode.EndOfDay
                && context.Settings.MinAccountBalanceOverride == 0
            )
            {
                context.Logger.LogError(
                    $"End of day drawdown accounts require Minimum Balance Override"
                );
                return;
            }

            // Check account filter
            if (context.Settings.CurrentAccount.Id != orderRequestParameters.Account.Id)
            {
                return;
            }

            // Idempotency check
            if (!_processedRequests.TryTrack(orderRequestParameters.RequestId))
            {
                context.Logger.LogInfo(
                    $"Order request {orderRequestParameters.RequestId} has already been processed - passing through unchanged"
                );
                return;
            }

            // Check for exit
            double netPosition = context.GetNetPositionQuantity(
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

            // Check for stop loss
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
                    return;
                }
                else if (context.Settings.MissingStopLossAction == MissingStopLossAction.Ignore)
                {
                    context.Logger.LogInfo(
                        $"Order request {orderRequestParameters.RequestId} has no stop loss - passing through unchanged"
                    );
                    return;
                }
            }

            // Infer drawdown mode
            string accountId = orderRequestParameters.Account.Id;
            DrawdownMode drawdownMode = InferDrawdownMode(accountId);

            // Wrap account
            var account = orderRequestParameters.Account;

            // Calculate risk capital
            string calculationReason = "";
            double riskCapital = RiskCalculator.CalculateRiskCapital(
                account,
                context.Settings.RiskPercent,
                drawdownMode,
                out calculationReason,
                minAccountBalanceOverride: context.Settings.MinAccountBalanceOverride
            );
            if (riskCapital <= 0)
            {
                context.Logger.LogInfo($"Risk=0 for {account.Id}. Reason: {calculationReason}");
                // If risk is 0, we can't trade.
                // For Request: Set Qty to 0 (Cancel)
                orderRequestParameters.Quantity = 0;
                return;
            }

            string calculationReason1 = "";
            var drawdown = RiskCalculator.GetAvailableDrawdown(
                account,
                account.InferDrawdownMode(),
                out calculationReason1,
                context.Settings.MinAccountBalanceOverride
            );
            context.Logger.LogInfo(
                $"Account balance:{account.Balance} drawdown: {drawdown} risk capital:{riskCapital}"
            );

            // Get symbol data
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

            // Caculate stop loss price
            var slTpHolder = orderRequestParameters.StopLossItems[0];
            double stopDistanceTicks = RiskCalculator.GetStopDistanceTicks(
                slTpHolder,
                tickSize,
                entryPrice
            );

            // Update symbol context for metrics
            context.Metrics.LastSymbol = symbol;
            context.Metrics.LastStopDistanceTicks = stopDistanceTicks;

            // Calculate position size
            int calculatedSize = RiskCalculator.CalculatePositionSize(
                riskCapital,
                stopDistanceTicks,
                tickValue
            );

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

                // Set current request quantity to 0 to kill the native SDK modification
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

        private static DrawdownMode InferDrawdownMode(string accountId)
        {
            if (TradingExtensions.IntradayAccountPattern().IsMatch(accountId))
            {
                return DrawdownMode.Intraday;
            }
            else if (TradingExtensions.EndOfDayAccountPattern().IsMatch(accountId))
            {
                return DrawdownMode.EndOfDay;
            }
            else
            {
                return DrawdownMode.Static;
            }
            // TODO: V2: Add UX override for drawdown mode
        }

        public void Dispose()
        {
            context.Dispose();
            _processedRequests.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
