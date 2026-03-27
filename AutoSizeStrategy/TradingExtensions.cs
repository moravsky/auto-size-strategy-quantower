using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TradingPlatform.BusinessLayer;

namespace AutoSizeStrategy
{
    public static partial class TradingExtensions
    {
        [GeneratedRegex(@"TPPRO\d+")]
        public static partial Regex IntradayAccountPattern();

        [GeneratedRegex(@"TPT\d+")]
        public static partial Regex EndOfDayAccountPattern();

        // Micro contracts: MNQ, MES, MGC, MYM, M2K, etc.
        // All start with "M" followed by uppercase letter.
        [GeneratedRegex("^M[A-Z]")]
        private static partial Regex MicroContractPattern();

        public static bool IsMicro(this ISymbol symbol)
        {
            return MicroContractPattern().IsMatch(symbol.Id);
        }

        public static double GetCommission(this IStrategySettings settings, ISymbol symbol) =>
            symbol.IsMicro() ? settings.CommissionMicro : settings.CommissionMini;

        public static DrawdownMode InferDrawdownMode(this IAccount account)
        {
            ArgumentNullException.ThrowIfNull(account);

            var id = account.Id;
            if (IntradayAccountPattern().IsMatch(id))
                return DrawdownMode.Intraday;
            else if (EndOfDayAccountPattern().IsMatch(id))
                return DrawdownMode.EndOfDay;
            else
                return DrawdownMode.Static;
        }

        public static bool IsExitDirection(this Side side, double netPosition)
        {
            if (netPosition > MathUtil.Epsilon)
                return side == Side.Sell;
            else if (netPosition < -MathUtil.Epsilon)
                return side == Side.Buy;
            else
                return false;
        }

        // Determines if an order is an exit (direction AND quantity <= current position).
        public static bool IsExitForPosition(this IOrder order, double netPosition)
        {
            return order.Side.IsExitDirection(netPosition)
                   && order.TotalQuantity <= Math.Abs(netPosition) + MathUtil.Epsilon;
        }

        // Determines if a request is an exit (direction AND quantity <= current position).
        public static bool IsExitForPosition(
            this IOrderRequestParameters orderRequestParameters,
            double netPosition
        )
        {
            return orderRequestParameters.Side.IsExitDirection(netPosition)
                   && orderRequestParameters.Quantity <= Math.Abs(netPosition) + MathUtil.Epsilon;
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
            var accountsList = accounts?.ToList();
            if (accountsList is not { Count: > 0 })
                return default;


            return accountsList.MinBy(item =>
            {
                // DUCK TYPING (The "C++ Template" Style)
                // We assume 'item' has a public property named 'Id'.
                // If it doesn't, this crashes (but only if Quantower removes Id from Account).
                dynamic d = item;
                var id = d.Id;
                return id switch
                {
                    // Priority 0: TPT PRO Intraday
                    var _ when IntradayAccountPattern().IsMatch(id) => 0,
                    // Priority 1: TPT Eval EOD
                    var _ when EndOfDayAccountPattern().IsMatch(id) => 1,
                    // Priority 2: Everything else
                    _ => 2,
                };
            });
        }

        public static bool TryGetInfoDouble(this IAccount account, string key, out double result)
        {
            result = 0;
            if (
                account.AdditionalInfo != null
                && account.AdditionalInfo.TryGetValue(key, out var valStr)
                && !string.IsNullOrWhiteSpace(valStr)
            )
            {
                // This will return true for "inf", "123.45", and "Infinity"
                bool success = valStr.TryParseDouble(out result);
                if (success)
                {
                    MathUtil.ValidateFinite(result, $"account.AdditionalInfo[{key}]");
                }

                return success;
            }

            return false;
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
