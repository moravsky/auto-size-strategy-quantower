using TradingPlatform.BusinessLayer;

namespace AutoSizeStrategy
{
    public static class OrderExtensions
    {
        /// Determines if an order is "Reduce-Only".
        /// A Reduce-Only order decreases the current position exposure.
        public static bool IsReduceOnly(this IOrder order, double netPosition)
        {
            if (netPosition > MathUtil.Epsilon)
            {
                return order.Side == Side.Sell;
            }
            else if (netPosition < -MathUtil.Epsilon)
            {
                return order.Side == Side.Buy;
            }
            else
            {
                // If we are flat, ANY order without SL is an opening order (not reduce-only)
                return false;
            }
        }
    }
}
