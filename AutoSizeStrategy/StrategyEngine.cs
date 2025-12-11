using System;
using System.Text.RegularExpressions;
using System.Threading;
using TradingPlatform.BusinessLayer;

namespace AutoSizeStrategy
{
    public partial class StrategyEngine(IStrategyContext context)
    {
        [GeneratedRegex(@"\[RiskQty:((?s).)+\]", RegexOptions.Compiled)]
        private static partial Regex TagRegex();

        [GeneratedRegex(@"TPPRO\d+", RegexOptions.Compiled)]
        private static partial Regex IntradayAccountPattern();

        [GeneratedRegex(@"TPT\d+", RegexOptions.Compiled)]
        private static partial Regex EndOfDayAccountPattern();

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
            if (
                placeOrderRequestParameters.Comment != null
                && placeOrderRequestParameters.Comment.Contains("[RiskQty:")
            )
            {
                context.Logger.LogInfo(
                    $"Order request {placeOrderRequestParameters.RequestId} has [RiskQty: comment - passing through unchanged"
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

            // Calculate position size using new overload
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

            // Update tag
            string tag = $"[RiskQty:{calculatedSize}]";

            // Append safely
            placeOrderRequestParameters.Comment = string.IsNullOrEmpty(
                placeOrderRequestParameters.Comment
            )
                ? tag
                : $"{placeOrderRequestParameters.Comment} {tag}";
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

            double correctSize;

            if (TryGetSizeFromTag(order.Comment, order.Id, out int taggedSize))
            {
                correctSize = taggedSize;
            }
            else
            {
                context.Logger.LogInfo(
                    $"Order {order.Id} does not have a size tag, using default of 2"
                );
                // TODO: Change ProcessFailSafe to verify order correctness a different way,
                // e.g. by checking the order size against the risk capital
                correctSize = 2;
            }

            if (Math.Abs(order.TotalQuantity - correctSize) > 0.001)
            {
                context.Logger.LogInfo(
                    $"Killing Order {order.Id}. Size is {order.TotalQuantity}, must be {correctSize}."
                );
                try
                {
                    order.Cancel();
                }
                catch (Exception ex)
                {
                    context.Logger.LogError($"Order {order.Id} cancelation failed: {ex.Message}");
                }
            }
        }

        public bool TryGetSizeFromTag(string comment, string orderId, out int taggedSize)
        {
            var match = TagRegex().Match(comment ?? "");
            if (match.Success && match.Groups.Count > 1 && match.Groups[1].Success)
            {
                if (int.TryParse(match.Groups[1].Value, out taggedSize))
                {
                    return true;
                }
                else
                {
                    context.Logger.LogError($"Order {orderId} has invalid validation tag.");
                }
            }
            taggedSize = 0;
            return false;
        }
    }
}
