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
        [Fact]
        public void Equals_SameValues_ReturnsTrue()
        {
            // Same numbers
            bool equal = MathUtil.Equals(123.456, 123.456);
            Assert.True(equal);
        }

        [Fact]
        public void Equals_ValuesDifferByLessThanEpsilon_ReturnsTrue()
        {
            // Difference smaller than Epsilon (1e-9)
            double a = 1.0000000005;
            double b = 1.0000000000;
            bool equal = MathUtil.Equals(a, b);
            Assert.True(equal);
        }

        [Fact]
        public void Equals_ValuesDifferByMoreThanEpsilon_ReturnsFalse()
        {
            // Difference larger than Epsilon
            double a = 1.00000001;
            double b = 1.0;
            bool equal = MathUtil.Equals(a, b);
            Assert.False(equal);
        }
    }
}
