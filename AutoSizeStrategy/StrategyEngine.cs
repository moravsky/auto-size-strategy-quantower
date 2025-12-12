using System;
using System.Text.RegularExpressions;
using System.Threading;
using TradingPlatform.BusinessLayer;

namespace AutoSizeStrategy
{
    // TODO: Run StrategyEngine on a backgroud thread to unblock UI during debugging
    public partial class StrategyEngine(IStrategyContext context) : IDisposable
    {
        [GeneratedRegex(@"\[RiskQty:((?s).)+\]", RegexOptions.Compiled)]
        private static partial Regex TagRegex();

        [GeneratedRegex(@"TPPRO\d+", RegexOptions.Compiled)]
        private static partial Regex IntradayAccountPattern();

        [GeneratedRegex(@"TPT\d+", RegexOptions.Compiled)]
        private static partial Regex EndOfDayAccountPattern();

        private readonly TrackingSet<long> _processedRequests = new();
        private readonly TrackingSet<string> _processedOrders = new();

        public void ProcessRequest(RequestParameters requestParameters)
        {
            // Delegate the "selection logic" to our Factory
            var wrapper = RequestParametersWrapper.Create(requestParameters);

            // Pass the result to the main logic
            ProcessRequest(wrapper);
        }

        public void ProcessRequest(IRequestParameters requestParameters)
        {
            if (requestParameters is not IPlaceOrderRequestParameters placeOrderRequestParameters)
                return;

            // Idempotency check
            if (!_processedRequests.TryTrack(placeOrderRequestParameters.RequestId))
            {
                context.Logger.LogInfo(
                    $"Order request {placeOrderRequestParameters.RequestId} has already been processed - passing through unchanged"
                );
                return;
            }

            // Check for stop loss
            if (
                placeOrderRequestParameters.StopLossItems == null
                || placeOrderRequestParameters.StopLossItems.Count == 0
            )
            {
                if (context.Settings.MissingStopLossAction == MissingStopLossAction.Reject)
                {
                    context.Logger.LogInfo(
                        $"Order request {placeOrderRequestParameters.RequestId} cancelled: stop loss required"
                    );
                    placeOrderRequestParameters.Quantity = 0;
                    return;
                }
                else if (context.Settings.MissingStopLossAction == MissingStopLossAction.Ignore)
                {
                    context.Logger.LogInfo(
                        $"Order request {placeOrderRequestParameters.RequestId} has no stop loss - passing through unchanged"
                    );
                    return;
                }
            }

            // Infer drawdown mode
            string accountId = placeOrderRequestParameters.Account.Id;
            DrawdownMode drawdownMode = InferDrawdownMode(accountId);

            // Wrap account
            var wrappedAccount = new AccountWrapper(placeOrderRequestParameters.Account);

            // Calculate risk capital
            double riskCapital = RiskCalculator.CalculateRiskCapital(
                wrappedAccount,
                context.Settings.RiskPercent,
                drawdownMode
            );

            // Get symbol data
            var symbol = placeOrderRequestParameters.Symbol;
            double entryPrice = placeOrderRequestParameters.Price;
            double stopPrice = placeOrderRequestParameters.StopLossItems[0].Price;
            double tickSize = symbol.TickSize;
            double tickValue = symbol.GetTickCost(symbol.Last);

            // Calculate position size
            int calculatedSize = RiskCalculator.CalculatePositionSize(
                riskCapital,
                entryPrice,
                stopPrice,
                tickSize,
                tickValue
            );

            if (calculatedSize == 0)
            {
                context.Logger.LogInfo("Risk too small for 1 contract");
                placeOrderRequestParameters.CancellationToken = new CancellationToken(
                    canceled: true
                );
                return;
            }

            // Set calculated size
            if (placeOrderRequestParameters.Quantity != calculatedSize)
            {
                context.Logger.LogInfo(
                    $"Changed request {placeOrderRequestParameters.RequestId} quantity from {placeOrderRequestParameters.Quantity} to {calculatedSize}"
                );
            }
            placeOrderRequestParameters.Quantity = calculatedSize;
        }

        public void ReportOrderRemoved(string orderId)
        {
            context.OrderKiller.ReportCancelledOrder(orderId);
        }

        private DrawdownMode InferDrawdownMode(string accountId)
        {
            if (IntradayAccountPattern().IsMatch(accountId))
            {
                return DrawdownMode.Intraday;
            }
            else if (EndOfDayAccountPattern().IsMatch(accountId))
            {
                return DrawdownMode.EndOfDay;
            }
            else
            {
                return DrawdownMode.Static;
            }
            // TODO: V2: Add UX override for drawdown mode
        }

        public void ProcessFailSafe(IOrder order)
        {
            if (order.Status != OrderStatus.Opened)
                return;

            // idempotency check
            if (!_processedOrders.TryTrack(order.Id))
                return;

            // Check for stop loss
            if (order.StopLossItems.Length == 0)
            {
                if (context.Settings.MissingStopLossAction == MissingStopLossAction.Reject)
                {
                    context.Logger.LogInfo($"Cancelling order {order.Id}: missing stop loss");
                    context.OrderKiller.Kill(order);
                    return;
                }
                else if (context.Settings.MissingStopLossAction == MissingStopLossAction.Ignore)
                {
                    context.Logger.LogInfo(
                        $"Order {order.Id} has no stop loss - passing through unchanged"
                    );
                    return;
                }
            }

            // Infer drawdown mode
            string accountId = order.Account.Id;
            DrawdownMode drawdownMode = InferDrawdownMode(accountId);

            // Wrap account
            var wrappedAccount = new AccountWrapper(order.Account);

            // Calculate risk capital
            double riskCapital = RiskCalculator.CalculateRiskCapital(
                wrappedAccount,
                context.Settings.RiskPercent,
                drawdownMode
            );

            // Get symbol data
            var symbol = order.Symbol;
            double entryPrice = order.Price;
            double stopPrice = order.StopLossItems[0].Price;
            double tickSize = symbol.TickSize;
            double tickValue = symbol.GetTickCost(symbol.Last);

            // Calculate position size
            int calculatedSize = RiskCalculator.CalculatePositionSize(
                riskCapital,
                entryPrice,
                stopPrice,
                tickSize,
                tickValue
            );

            if (Math.Abs(order.TotalQuantity - calculatedSize) > 0.001)
            {
                context.Logger.LogInfo(
                    $"Killing Order {order.Id}. Size is {order.TotalQuantity}, must be {calculatedSize}."
                );
                context.OrderKiller.Kill(order);
            }
        }

        public void Dispose()
        {
            context.Dispose();
            _processedOrders.Dispose();
            _processedRequests.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
