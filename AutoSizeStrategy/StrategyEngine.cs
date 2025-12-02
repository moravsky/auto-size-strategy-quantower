using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TradingPlatform.BusinessLayer;

namespace AutoSizeStrategy
{
    public interface IStrategyLogger
    {
        void LogError(string message);
    }
    internal class StrategyEngine
    {
        private const int TARGET_QTY = 2;
        private readonly IStrategyLogger _logger;

        public StrategyEngine(IStrategyLogger logger) 
        {
            _logger = logger;
        }
        public void ProcessRequest(RequestParameters requestParameters)
        {
            if (requestParameters is not PlaceOrderRequestParameters placeOrderRequestParameters) return;

            string tag = $"[RiskQty:{TARGET_QTY}]";

            // Idempotency check
            if (placeOrderRequestParameters.Comment != null && placeOrderRequestParameters.Comment.Contains("[RiskQty:")) return;

            // Append safely
            placeOrderRequestParameters.Comment = string.IsNullOrEmpty(placeOrderRequestParameters.Comment) ? tag : $"{placeOrderRequestParameters.Comment} {tag}";

            // Enforce Size
            placeOrderRequestParameters.Quantity = TARGET_QTY;
        }
        public void ProcessFailSafe(IOrder order)
        {
            order.Cancel();
            
            if (order.Status != OrderStatus.Opened || order.IsReduceOnly) return;

            double correctSize;

            if (TryGetSizeFromTag(order.Comment, out int taggedSize))
            {
                correctSize = taggedSize;
            }
            else
            {
                correctSize = TARGET_QTY;
                _logger.LogError($"[WARNING] Order {order.Id} has no validation tag. Checked logic manually.");
            }

            // Using 0.001 tolerance for double comparison
            if (Math.Abs(order.TotalQuantity - correctSize) > 0.001)
            {
                _logger.LogError($"[FAIL-SAFE] Killing Order {order.Id}. Size is {order.TotalQuantity}, must be {correctSize}.");
                order.Cancel();
            }
        }
        public bool TryGetSizeFromTag(string comment, out int taggedSize)
        {
            var match = System.Text.RegularExpressions.Regex.Match(comment ?? "", @"\[RiskQty:(\d+)\]");
            if (match.Success)
            {
                return int.TryParse(match.Groups[1].Value, out taggedSize);
            }
            taggedSize = 0;
            return false;
        }
    }
}
