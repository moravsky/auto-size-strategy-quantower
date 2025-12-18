using TradingPlatform.BusinessLayer;

namespace AutoSizeStrategy
{
    public static class TradingExtensions
    {
        /// Determines if a trading operartion side is "Reduce-Only".
        /// A Reduce-Only operation decreases the current position exposure.
        public static bool IsReduceOnly(this Side side, double netPosition)
        {
            if (netPosition > MathUtil.Epsilon)
            {
                return side == Side.Sell;
            }
            else if (netPosition < -MathUtil.Epsilon)
            {
                return side == Side.Buy;
            }
            else
            {
                // If we are flat, ANY order without SL is an opening order (not reduce-only)
                return false;
            }
        }

        // Determines if an order is Reduce-Only
        public static bool IsReduceOnly(this IOrder order, double netPosition)
        {
            return order.Side.IsReduceOnly(netPosition);
        }

        // Determines if a request is Reduce-Only
        public static bool IsReduceOnly(
            this IOrderRequestParameters orderRequestParameters,
            double netPosition
        )
        {
            return orderRequestParameters.Side.IsReduceOnly(netPosition);
        }
    }
}
