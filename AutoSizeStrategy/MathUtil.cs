using System;

namespace AutoSizeStrategy
{
    public static class MathUtil
    {
        // Centralized constant: 1 billionth precision
        // (Safe for Crypto 8 decimals and Futures)
        public const double Epsilon = 1e-9;

        /// <summary>
        /// Safe equality check for doubles
        /// </summary>
        public static bool Equals(double a, double b)
        {
            return Math.Abs(a - b) < Epsilon;
        }
    }
}
