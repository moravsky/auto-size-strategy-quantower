using System;
using System.Text.RegularExpressions;
using System.Threading;
using TradingPlatform.BusinessLayer;

namespace AutoSizeStrategy
{
    // TODO: Run StrategyEngine on a backgroud thread to unblock UI during debugging
    public partial class StrategyEngine(IStrategyContext context) : IDisposable
    {
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
            if (requestParameters is not IOrderRequestParameters orderRequestParameters)
                return;

            // Idempotency check
            if (!_processedRequests.TryTrack(orderRequestParameters.RequestId))
            {
                context.Logger.LogInfo(
                    $"Order request {orderRequestParameters.RequestId} has already been processed - passing through unchanged"
                );
                return;
            }

            // Check for Reduce-Only
            double netPosition = context.GetNetPositionQuantity(
                orderRequestParameters.Account,
                orderRequestParameters.Symbol
            );

            if (orderRequestParameters.IsReduceOnlyForPosition(netPosition))
            {
                context.Logger.LogInfo(
                    $"Request {orderRequestParameters.RequestId} is Reduce-Only (NetPos: {netPosition}) - passing through unchanged."
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
            var wrappedAccount = new AccountWrapper(orderRequestParameters.Account);

            // Calculate risk capital
            double riskCapital = RiskCalculator.CalculateRiskCapital(
                wrappedAccount,
                context.Settings.RiskPercent,
                drawdownMode
            );

            // Get symbol data
            var symbol = orderRequestParameters.Symbol;
            double entryPrice = orderRequestParameters.Price;
            double tickSize = symbol.TickSize;
            double tickValue = symbol.GetTickCost(symbol.Last);

            // Caculate stop loss price
            var slTpHolder = orderRequestParameters.StopLossItems[0];
            double stopDistanceTicks = RiskCalculator.GetStopDistanceTicks(
                slTpHolder,
                tickSize,
                entryPrice
            );

            // Calculate position size
            int calculatedSize = RiskCalculator.CalculatePositionSize(
                riskCapital,
                stopDistanceTicks,
                tickValue
            );

            if (calculatedSize == 0)
            {
                context.Logger.LogInfo("Risk too big even for 1 contract");
            }

            // Set calculated size
            if (orderRequestParameters.Quantity != calculatedSize)
            {
                context.Logger.LogInfo(
                    $"Changed request {orderRequestParameters.RequestId} quantity from {orderRequestParameters.Quantity} to {calculatedSize}"
                );
            }
            orderRequestParameters.Quantity = calculatedSize;
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

            // Check for Reduce-Only
            double netPosition = context.GetNetPositionQuantity(order.Account, order.Symbol);
            if (order.IsReduceOnlyForPosition(netPosition))
            {
                context.Logger.LogInfo(
                    $"Order {order.Id} is Reduce-Only - passing through unchanged"
                );
                return;
            }

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
            double tickSize = symbol.TickSize;
            double tickValue = symbol.GetTickCost(symbol.Last);

            // Caculate stop loss price
            var slTpHolder = order.StopLossItems[0];
            double stopDistanceTicks = RiskCalculator.GetStopDistanceTicks(
                slTpHolder,
                tickSize,
                entryPrice
            );

            // Calculate position size
            int calculatedSize = RiskCalculator.CalculatePositionSize(
                riskCapital,
                stopDistanceTicks,
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
