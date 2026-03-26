using System;
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
        /// Calculates the maximum position size based on the position risk, stop distance, and tick value.
        public static int CalculatePositionSize(
            double positionRisk,
            double stopDistanceTicks,
            double tickValue
        )
        {
            MathUtil.ValidateFinite(positionRisk, nameof(positionRisk));
            MathUtil.ValidateFinite(stopDistanceTicks, nameof(stopDistanceTicks));
            MathUtil.ValidateFinite(tickValue, nameof(tickValue));

            // Could happen in a valid scenario
            if (positionRisk <= 0)
            {
                return 0;
            }

            // This is a configuration error if false (Impossible Instrument).
            if (tickValue <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tickValue), "Tick value must be positive");
            }

            // Stop distance must be positive to avoid Division by Zero.
            // If stop is 0, logic is broken (Entry == Stop).
            if (stopDistanceTicks <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(stopDistanceTicks), "Stop distance must be > 0");
            }

            // Calculate position size
            double rawResult = positionRisk / (stopDistanceTicks * tickValue);
            int positionSize = (int)Math.Floor(rawResult + MathUtil.Epsilon);

            return positionSize;
        }

        public static int CalculatePositionSize(
            double positionRisk,
            double entryPrice,
            double stopPrice,
            double tickSize,
            double tickValue
        )
        {
            MathUtil.ValidateFinite(positionRisk, nameof(positionRisk));
            MathUtil.ValidateFinite(entryPrice, nameof(entryPrice));
            MathUtil.ValidateFinite(stopPrice, nameof(stopPrice));
            MathUtil.ValidateFinite(tickSize, nameof(tickSize));
            MathUtil.ValidateFinite(tickValue, nameof(tickValue));

            if (tickSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tickSize), "Tick size must be positive");
            }

            if (tickValue <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tickValue), "Tick value must be positive");
            }

            // Calculate stop distance in ticks
            double stopDistanceTicks = Math.Round(
                Math.Abs(entryPrice - stopPrice) / tickSize,
                MidpointRounding.AwayFromZero
            );

            if (stopDistanceTicks <= 0)
            {
                return 0;
            }

            // Calculate position size
            return CalculatePositionSize(positionRisk, stopDistanceTicks, tickValue);
        }

        public static double GetStopDistanceTicks(
            SlTpHolder slTpHolder,
            double tickSize,
            double entryPrice
        )
        {
            MathUtil.ValidateFinite(slTpHolder.Price, nameof(slTpHolder) + ".Price");
            MathUtil.ValidateFinite(tickSize, nameof(tickSize));
            MathUtil.ValidateFinite(entryPrice, nameof(entryPrice));

            if (tickSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tickSize), "Tick size must be positive");
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

        // Raw dollar amount between current balance and the bust level.
        public static double GetAvailableDrawdown(
            IAccount account,
            DrawdownMode mode,
            out string reason,
            double minAccountBalanceOverride = 0.0
        )
        {
            MathUtil.ValidateFinite(minAccountBalanceOverride, nameof(minAccountBalanceOverride));
            ArgumentNullException.ThrowIfNull(account);

            double availableDrawdown;
            reason = "";

            if (minAccountBalanceOverride > 0)
            {
                reason =
                    $"Using minAccountBalanceOverride={minAccountBalanceOverride} for drawdown calculation";
                availableDrawdown = account.Balance - minAccountBalanceOverride;
            }
            else
            {
                switch (mode)
                {
                    case DrawdownMode.Static:
                        availableDrawdown = account.Balance;
                        break;
                    case DrawdownMode.Intraday:
                        if (
                            !account.TryGetInfoDouble(
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
                        reason = "EOD mode requires 'Minimum Account Balance (Override)' to be set";
                        return 0; // FAIL SAFE
                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(mode),
                            "Unsupported drawdown mode."
                        );
                }
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

            if (string.IsNullOrEmpty(reason))
            {
                reason = $"OK Drawdown: {availableDrawdown:F2}";
            }

            return availableDrawdown;
        }

        /// Determines the amount of capital that can be risked based on the account balance,
        /// previous EOD balance (for EOD accounts only) and the selected drawdown mode, then applies the risk percentage.
        public static double CalculatePositionRisk(
            IAccount account,
            double riskPercent,
            DrawdownMode mode,
            out string reason,
            double minAccountBalanceOverride = 0.0
        )
        {
            MathUtil.ValidateFinite(riskPercent, nameof(riskPercent));

            if (riskPercent <= 0 || riskPercent > 100)
                throw new ArgumentOutOfRangeException(
                    nameof(riskPercent),
                    "Risk percentage must be > 0 and <= 100."
                );

            double availableDrawdown = GetAvailableDrawdown(
                account,
                mode,
                out reason,
                minAccountBalanceOverride
            );

            if (availableDrawdown <= 0)
                return 0;

            double positionRisk = availableDrawdown * (riskPercent / 100.0);
            reason = $"OK Drawdown: {availableDrawdown:F2}, Risk: {positionRisk:F2}";
            return positionRisk;
        }
    }
}
