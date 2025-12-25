// AutoSizeStrategy.Test\MathUtilTests.cs
using System;
using AutoSizeStrategy;
using Xunit;

namespace AutoSizeStrategy.Test
{
    /// <summary>
    /// Tests for <see cref="MathUtil"/>.
    /// </summary>
    public class MathUtilTests
    {
        [Theory]
        // Exact Match
        [InlineData(123.456, 123.456, true)]
        // Inside Epsilon (1e-9 is the limit, so 1e-10 is safe)
        [InlineData(1.0, 1.0000000001, true)]
        // Outside Epsilon (1e-8 is too big)
        [InlineData(1.0, 1.00000001, false)]
        // Negative numbers logic
        [InlineData(-1.0, -1.0000000001, true)]
        [InlineData(-1.0, -1.00000001, false)]
        public void Equals_HandlesPrecisionCorrectly(double a, double b, bool expected)
        {
            Assert.Equal(expected, MathUtil.Equals(a, b));
        }

        [Theory]
        [InlineData(Double.NegativeInfinity)]
        [InlineData(Double.PositiveInfinity)]
        [InlineData(Double.NaN)]
        public void CheckFinite_Throws_NotFinite(double value)
        {
            Assert.Throws<ArgumentException>(() => MathUtil.ValidateFinite(value));
        }
    }
}
