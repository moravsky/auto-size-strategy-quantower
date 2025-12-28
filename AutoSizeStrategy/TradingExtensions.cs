using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TradingPlatform.BusinessLayer;

namespace AutoSizeStrategy
{
    public static class TradingExtensions
    {
        private const int DefaultRetryIntervalMs = 200;
        private const int DefaultMaxWaitMs = 2000;

        // Determines if a side is exit given a KNOWN net position.
        public static bool IsExitForPosition(this Side side, double netPosition)
        {
            if (netPosition > MathUtil.Epsilon)
                return side == Side.Sell;
            else if (netPosition < -MathUtil.Epsilon)
                return side == Side.Buy;
            else
                return false;
        }

        // Determines if an order is exit given a KNOWN net position.
        public static bool IsExitForPosition(this IOrder order, double netPosition)
        {
            return order.Side.IsExitForPosition(netPosition);
        }

        // Determines if a request is exit given a KNOWN net position.
        public static bool IsExitForPosition(
            this IOrderRequestParameters orderRequestParameters,
            double netPosition
        )
        {
            return orderRequestParameters.Side.IsExitForPosition(netPosition);
        }

        // Determines if an order is exit, handling potential race conditions
        // by polling the context until a position appears or timeout occurs.
        public static async Task<bool> IsExitAsync(
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
                    return order.IsExitForPosition(netPos);
                }

                if (DateTime.UtcNow >= deadline)
                {
                    return false; // Assumed Naked Entry
                }

                await Task.Delay(pollTime);
            }
        }

        public static double GetLikelyFillPrice(
            this IOrderRequestParameters orderRequestParameters
        ) =>
            orderRequestParameters.OrderTypeId switch
            {
                OrderType.Market => orderRequestParameters.Symbol.Last,
                OrderType.Limit or OrderType.StopLimit or OrderType.LimitIfTouched =>
                    orderRequestParameters.Price,
                OrderType.Stop or OrderType.MarketIfTouched or OrderType.TrailingStop =>
                    orderRequestParameters.TriggerPrice,
                _ => throw new NotSupportedException("Order type not supported"),
            };

        public static double GetLikelyFillPrice(this IOrder order) =>
            order.OrderTypeId switch
            {
                OrderType.Market => order.Symbol.Last,
                OrderType.Limit or OrderType.StopLimit or OrderType.LimitIfTouched => order.Price,
                OrderType.Stop or OrderType.MarketIfTouched or OrderType.TrailingStop =>
                    order.TriggerPrice,
                _ => throw new NotSupportedException("Order type not supported"),
            };

        // This accepts ANY object T, as long as it can find the 'id'.
        // We need this, because we cannot create SDK Account type.
        public static T FindTargetAccount<T>(this IEnumerable<T> accounts)
        {
            if (accounts == null || !accounts.Any())
                return default;

            return accounts.MinBy(item =>
            {
                // DUCK TYPING (The "C++ Template" Style)
                // We assume 'item' has a public property named 'Id'.
                // If it doesn't, this crashes (but only if Quantower removes Id from Account).
                dynamic d = item;
                var id = d.Id;
                return id switch
                {
                    // Priority 0: TPT PRO Intraday
                    var _ when StrategyEngine.IntradayAccountPattern().IsMatch(id) => 0,
                    // Priority 1: TPT Eval EOD
                    var _ when StrategyEngine.EndOfDayAccountPattern().IsMatch(id) => 1,
                    // Priority 2: Everything else
                    _ => 2,
                };
            });
        }

        // Just like double.TryParse, but handles "inf" and "Infinity"
        public static bool TryParseDouble(this string value, out double result)
        {
            // Try standard parsing first (handles "Infinity", "1.23", etc.)
            if (
                double.TryParse(
                    value,
                    System.Globalization.NumberStyles.Float
                        | System.Globalization.NumberStyles.AllowThousands,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out result
                )
            )
            {
                return true;
            }

            // Fallback for shorthand broker symbols that standard .NET misses
            string clean = value?.Trim().ToLowerInvariant();
            if (clean == "inf" || clean == "+inf")
            {
                result = double.PositiveInfinity;
                return true;
            }

            if (clean == "-inf")
            {
                result = double.NegativeInfinity;
                return true;
            }

            return false;
        }
    }
}
