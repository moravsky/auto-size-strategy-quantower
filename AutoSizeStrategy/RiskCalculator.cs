using System;
using System.Timers;
using TradingPlatform.BusinessLayer;

namespace AutoSizeStrategy
{
    /// <summary>
    /// Indicates the type of drawdown to use when calculating available risk capital.
    /// </summary>
    public enum DrawdownMode
    {
        Intraday,
        EndOfDay,
        Static,
    }

    /* * CAUTIONARY TALE: THE $104 MILLION PENNY
    * On April 20, 2020, Oil futures went to -$37.63.
    * Interactive Brokers' software assumed prices couldn't go below zero
    * and displayed the price as $0.01.
    * * Result: Users bought the "dip" at negative prices, yet the prices kept going lower.
    * * The liquidation engine failed to trigger, and IBKR lost ~$104 million covering client debts.
    * * LESSON: NEVER assume values (Prices, Balances, Risk) are always positive.
    * Always validate inputs and handle "impossible" edge cases.
    */
    public static class RiskCalculator
    {
        // TODO: factor commisions and slippage into calculation
        /// Calculates the maximum position size based on the risk capital, stop distance, and tick value.
        public static int CalculatePositionSize(
            double riskCapital,
            double stopDistanceTicks,
            double tickValue
        )
        {
            if (
                double.IsNaN(riskCapital)
                || double.IsInfinity(riskCapital)
                || double.IsNaN(stopDistanceTicks)
                || double.IsInfinity(stopDistanceTicks)
                || double.IsNaN(tickValue)
                || double.IsInfinity(tickValue)
            )
            {
                throw new ArgumentException("Input values must be finite numbers");
            }

            // Could happen in a valid scenario
            if (riskCapital <= 0)
            {
                return 0;
            }

            // This is a configuration error if false (Impossible Instrument).
            if (tickValue <= 0)
            {
                throw new ArgumentException("Tick value must be positive", nameof(tickValue));
            }

            // Stop distance must be positive to avoid Division by Zero.
            // If stop is 0, logic is broken (Entry == Stop).
            if (stopDistanceTicks <= 0)
            {
                throw new ArgumentException("Stop distance must be > 0", nameof(stopDistanceTicks));
            }

            // Calculate position size
            double rawResult = riskCapital / (stopDistanceTicks * tickValue);
            int positionSize = (int)Math.Floor(rawResult + MathUtil.Epsilon);

            return positionSize;
        }

        public static int CalculatePositionSize(
            double riskCapital,
            double entryPrice,
            double stopPrice,
            double tickSize,
            double tickValue
        )
        {
            if (
                double.IsNaN(riskCapital)
                || double.IsInfinity(riskCapital)
                || double.IsNaN(entryPrice)
                || double.IsInfinity(entryPrice)
                || double.IsNaN(stopPrice)
                || double.IsInfinity(stopPrice)
                || double.IsNaN(tickSize)
                || double.IsInfinity(tickSize)
                || double.IsNaN(tickValue)
                || double.IsInfinity(tickValue)
            )
            {
                throw new ArgumentException("Input values must be finite numbers");
            }

            if (tickSize <= 0)
            {
                throw new ArgumentException("Tick size must be positive");
            }

            if (tickValue <= 0)
            {
                throw new ArgumentException("Tick value must be positive");
            }

            // Calculate stop distance in ticks
            double stopDistanceTicks = Math.Round(
                Math.Abs(entryPrice - stopPrice) / tickSize,
                MidpointRounding.AwayFromZero
            );

            // Calculate position size
            return CalculatePositionSize(riskCapital, stopDistanceTicks, tickValue);
        }

        public static double GetStopDistanceTicks(
            SlTpHolder slTpHolder,
            double tickSize,
            double entryPrice
        )
        {
            if (tickSize <= 0)
            {
                throw new ArgumentException("Tick size must be positive");
            }
            if (double.IsNaN(entryPrice) || double.IsInfinity(entryPrice))
            {
                throw new ArgumentException("Input values must be finite numbers");
            }

            if (slTpHolder.PriceMeasurement == PriceMeasurement.Offset)
            {
                return slTpHolder.Price;
            }
            else if (slTpHolder.PriceMeasurement == PriceMeasurement.Absolute)
            {
                return Math.Round(
                    Math.Abs(entryPrice - slTpHolder.Price) / tickSize,
                    MidpointRounding.AwayFromZero
                );
            }
            else
            {
                throw new ArgumentException("Price measurement must be Offset or Absolute");
            }
        }

        /// Determines the amount of capital that can be risked based on the account balance,
        /// previous EOD balance (for EOD accounts only) and the selected drawdown mode, then applies the risk percentage.
        public static double CalculateRiskCapital(
            IAccount account,
            double riskPercent,
            DrawdownMode mode,
            out string reason
        )
        {
            ArgumentNullException.ThrowIfNull(account);

            if (riskPercent <= 0 || riskPercent > 100)
                throw new ArgumentException(
                    "Risk percentage must be > 0 and <= 100.",
                    nameof(riskPercent)
                );

            double availableDrawdown = 0;
            reason = "";
            switch (mode)
            {
                case DrawdownMode.Static:
                    availableDrawdown = account.Balance;
                    break;
                case DrawdownMode.Intraday:
                    if (
                        !TryGetInfoDouble(
                            account,
                            "AutoLiquidateThresholdCurrentValue",
                            out double autoLiqCurrent
                        )
                    )
                    {
                        reason = "Missing 'AutoLiquidateThresholdCurrentValue' in Account Info";
                        return 0; // FAIL SAFE
                    }
                    availableDrawdown = account.Balance - autoLiqCurrent;
                    break;
                case DrawdownMode.EndOfDay:
                    if (
                        !TryGetInfoDouble(
                            account,
                            "AutoLiquidateThreshold",
                            out double drawdownSize
                        )
                    )
                    {
                        reason = "Missing 'AutoLiquidateThreshold' (Drawdown Size)";
                        return 0;
                    }
                    if (!TryGetInfoDouble(account, "MinAccountBalance", out double minBalance))
                    {
                        reason = "Missing 'MinAccountBalance'";
                        return 0;
                    }
                    if (!TryGetInfoDouble(account, "NetPnL", out double netPnl))
                    {
                        reason = "Missing 'NetPnL' from the broker, assume it's 0";
                        netPnl = 0;
                    }
                    // Risk budget is the lesser of:
                    // 1. Distance to the hard floor (Balance - MinBalance)
                    // 2. Daily Budget (DrawdownSize + NetPnL)
                    availableDrawdown = Math.Min(
                        account.Balance - minBalance,
                        drawdownSize + netPnl
                    );
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(mode),
                        "Unsupported drawdown mode."
                    );
            }

            if (availableDrawdown <= 0)
            {
                reason = $"Available drawdown is zero or negative ({availableDrawdown})";
                return 0;
            }

            if (availableDrawdown > account.Balance)
            {
                // Edge case: Bad data shouldn't allow risking more than the entire account
                availableDrawdown = account.Balance;
                reason = "Drawdown capped at Account Balance (Calculation exceeded balance)";
            }

            double riskCapital = availableDrawdown * (riskPercent / 100.0);
            if (string.IsNullOrEmpty(reason))
            {
                reason = $"OK Drawdown: {availableDrawdown:F2}, Risk: {riskCapital:F2}";
            }
            return riskCapital;
        }

        private static bool TryGetInfoDouble(IAccount account, string key, out double result)
        {
            result = 0;
            if (
                account.AdditionalInfo != null
                && account.AdditionalInfo.TryGetValue(key, out var valStr)
                && !string.IsNullOrWhiteSpace(valStr)
            )
            {
                // InvariantCulture prevents issues if server sends "145.500" vs "145,500"
                return double.TryParse(
                    valStr,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out result
                );
            }
            return false;
        }
    }
}
