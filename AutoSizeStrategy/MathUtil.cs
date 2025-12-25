using System;

namespace AutoSizeStrategy
{
    public static class MathUtil
    {
        // Centralized constant: 1 billionth precision
        // (Safe for Crypto and Futures: 8 decimals)
        public const double Epsilon = 1e-9;

        /// <summary>
        /// Safe equality check for doubles
        /// </summary>
        public static bool Equals(double a, double b)
        {
            return Math.Abs(a - b) < Epsilon;
        }

        public static void ValidateFinite(double value, string name = "value")
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentException($"Value must be finite, was {value}", name);
            }
        }
    }
}
