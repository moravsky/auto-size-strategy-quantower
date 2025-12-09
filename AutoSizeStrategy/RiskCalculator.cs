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

    public static class RiskCalculator
    {
        /// Calculates the maximum position size based on the risk capital, stop distance, and tick value.
        public static int CalculatePositionSize(
            double riskCapital,
            double stopDistanceTicks,
            double tickValue
        )
        {
            // Validate arguments
            if (
                double.IsNaN(riskCapital)
                || double.IsInfinity(riskCapital)
                || double.IsNaN(stopDistanceTicks)
                || double.IsInfinity(stopDistanceTicks)
                || double.IsNaN(tickValue)
                || double.IsInfinity(tickValue)
            )
            {
                throw new ArgumentException("Input values must be finite numbers.");
            }

            if (riskCapital <= 0 || stopDistanceTicks <= 0 || tickValue <= 0)
            {
                throw new ArgumentException("Input values must be greater than zero.");
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
            // Calculate stop distance in ticks
            double stopDistanceTicks = Math.Abs(entryPrice - stopPrice) / tickSize;

            // Calculate position size
            return CalculatePositionSize(riskCapital, stopDistanceTicks, tickValue);
        }

        /// Determines the amount of capital that can be risked based on the account balance
        /// and the selected drawdown mode, then applies the risk percentage.
        public static double CalculateRiskCapital(
            IAccount account,
            double riskPercent,
            DrawdownMode mode
        )
        {
            ArgumentNullException.ThrowIfNull(account);

            if (riskPercent <= 0 || riskPercent > 100)
                throw new ArgumentException(
                    "Risk percentage must be > 0 and <= 100.",
                    nameof(riskPercent)
                );

            var availableDrawdown = mode switch
            {
                DrawdownMode.Static => account.Balance,
                DrawdownMode.Intraday or DrawdownMode.EndOfDay => account.Balance - 145_500, // Hard‑coded buffer for now – replace with real logic later.
                _ => throw new ArgumentOutOfRangeException(
                    nameof(mode),
                    "Unsupported drawdown mode."
                ),
            };
            double riskCapital = availableDrawdown * (riskPercent / 100.0);
            return riskCapital;
        }
    }
}
