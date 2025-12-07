using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TradingPlatform.BusinessLayer;

namespace AutoSizeStrategy
{
    public partial class StrategyEngine(IStrategyLogger logger)
    {
        private const int TARGET_QTY = 2;

        [GeneratedRegex(@"\[RiskQty:((?s).)+\]", RegexOptions.Compiled)]
        private static partial Regex TagRegex();

        public void ProcessRequest(RequestParameters requestParameters)
        {
            if (requestParameters is not PlaceOrderRequestParameters placeOrderRequestParameters)
                return;

            string tag = $"[RiskQty:{TARGET_QTY}]";

            // Idempotency check
            if (
                placeOrderRequestParameters.Comment != null
                && placeOrderRequestParameters.Comment.Contains("[RiskQty:")
            )
                return;

            // Append safely
            placeOrderRequestParameters.Comment = string.IsNullOrEmpty(
                placeOrderRequestParameters.Comment
            )
                ? tag
                : $"{placeOrderRequestParameters.Comment} {tag}";

            // Enforce Size
            if (placeOrderRequestParameters.Quantity != TARGET_QTY)
            {
                logger.LogInfo(
                    $"[Risk Enforced] Adjusting order {placeOrderRequestParameters.RequestId}' quantity from {placeOrderRequestParameters.Quantity} to {TARGET_QTY}"
                );
            }
            placeOrderRequestParameters.Quantity = TARGET_QTY;
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
                logger.LogInfo(
                    $"[FAIL-SAFE] Order {order.Id} does not have a size tag, using default of {TARGET_QTY}"
                );
                correctSize = TARGET_QTY;
            }

            // Using 0.001 tolerance for double comparison
            if (Math.Abs(order.TotalQuantity - correctSize) > 0.001)
            {
                logger.LogInfo(
                    $"[FAIL-SAFE] Killing Order {order.Id}. Size is {order.TotalQuantity}, must be {correctSize}."
                );
                try
                {
                    order.Cancel();
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        $"[FAIL-SAFE] Order {order.Id} cancelation failed: {ex.Message}"
                    );
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
                    logger.LogError($"[FAIL-SAFE] Order {orderId} has invalid validation tag.");
                }
            }
            taggedSize = 0;
            return false;
        }
    }
}
