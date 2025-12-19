using System;
using System.Threading.Tasks;
using TradingPlatform.BusinessLayer;

namespace AutoSizeStrategy
{
    public static class TradingExtensions
    {
        private const int DefaultRetryIntervalMs = 200;
        private const int DefaultMaxWaitMs = 2000;

        // Determines if a side is reduce-only given a KNOWN net position.
        public static bool IsReduceOnlyForPosition(this Side side, double netPosition)
        {
            if (netPosition > MathUtil.Epsilon)
                return side == Side.Sell;
            else if (netPosition < -MathUtil.Epsilon)
                return side == Side.Buy;
            else
                return false;
        }

        // Determines if an order is reduce-only given a KNOWN net position.
        public static bool IsReduceOnlyForPosition(this IOrder order, double netPosition)
        {
            return order.Side.IsReduceOnlyForPosition(netPosition);
        }

        // Determines if a request is reduce-only given a KNOWN net position.
        public static bool IsReduceOnlyForPosition(
            this IOrderRequestParameters orderRequestParameters,
            double netPosition
        )
        {
            return orderRequestParameters.Side.IsReduceOnlyForPosition(netPosition);
        }

        // Determines if an order is Reduce-Only, handling potential race conditions
        // by polling the context until a position appears or timeout occurs.
        public static async Task<bool> IsReduceOnlyAsync(
            this IOrder order,
            IStrategyContext context,
            TimeSpan? maxWait = null,
            TimeSpan? retryInterval = null
        )
        {
            var waitTime = maxWait ?? TimeSpan.FromMilliseconds(DefaultMaxWaitMs);
            var pollTime = retryInterval ?? TimeSpan.FromMilliseconds(DefaultRetryIntervalMs);
            var deadline = DateTime.UtcNow.Add(waitTime);

            while (true)
            {
                double netPos = context.GetNetPositionQuantity(order.Account, order.Symbol);

                if (Math.Abs(netPos) > MathUtil.Epsilon)
                {
                    return order.IsReduceOnlyForPosition(netPos);
                }

                if (DateTime.UtcNow >= deadline)
                {
                    return false; // Assumed Naked Entry
                }

                await Task.Delay(pollTime);
            }
        }
    }
}
