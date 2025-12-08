using System;

namespace AutoSizeStrategy
{
    public static class RiskCalculator
    {
        /// <summary>
        /// Calculates the position size based on risk capital, stop distance in ticks, and tick value.
        /// </summary>
        /// <param name="riskCapital">The amount of capital available for risk.</param>
        /// <param name="stopDistanceTicks">The distance of the stop in ticks.</param>
        /// <param name="tickValue">The monetary value per tick.</param>
        /// <returns>The maximum position size as an integer.</returns>
        /// <exception cref="ArgumentException">
        /// Thrown if any input is NaN, Infinity, or less than or equal to zero, or if
        /// the calculated position size is zero (indicating the risk capital is too small).
        /// </exception>
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
    }
}
