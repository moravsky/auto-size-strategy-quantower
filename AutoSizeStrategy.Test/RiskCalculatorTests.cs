// AutoSizeStrategy.Test\RiskCalculatorTests.cs
using System;
using AutoSizeStrategy;
using Xunit;

namespace AutoSizeStrategy.Test
{
    /// <summary>
    /// Unit tests for <see cref="RiskCalculator"/>.
    /// </summary>
    public class RiskCalculatorTests
    {
        [Fact]
        public void CalculatePositionSize_StandardCase_ReturnsCorrectSize()
        {
            // $500 risk, 20 tick stop, $5/tick = 5 contracts
            int size = RiskCalculator.CalculatePositionSize(500, 20, 5);
            Assert.Equal(5, size);
        }

        [Fact]
        public void CalculatePositionSize_RoundsDown_WhenNotExactFit()
        {
            // $500 risk, 30 tick stop, $5/tick = 3 contracts (not 3.33)
            int size = RiskCalculator.CalculatePositionSize(500, 30, 5);
            Assert.Equal(3, size);
        }

        [Fact]
        public void CalculatePositionSize_MNQ_CalculatesCorrectly()
        {
            // $100 risk, 40 tick stop, $0.50/tick = 5 contracts
            int size = RiskCalculator.CalculatePositionSize(100, 40, 0.5);
            Assert.Equal(5, size);
        }

        [Fact]
        public void CalculatePositionSize_MGC_CalculatesCorrectly()
        {
            // Example: $300 risk, 25‑tick stop, $4 per tick = 3 contracts
            int size = RiskCalculator.CalculatePositionSize(300, 25, 4);
            Assert.Equal(3, size);
        }

        [Theory]
        [InlineData(0, 20, 5)] // zero risk
        [InlineData(-100, 20, 5)] // negative risk
        [InlineData(500, 0, 5)] // zero stop
        [InlineData(500, 20, 0)] // zero tick value
        public void CalculatePositionSize_InvalidInputs_ThrowsArgumentException(
            decimal risk,
            decimal stop,
            decimal tickVal
        )
        {
            Assert.Throws<ArgumentException>(() =>
                RiskCalculator.CalculatePositionSize((double)risk, (double)stop, (double)tickVal)
            );
        }

        [Fact]
        public void CalculatePositionSize_RiskTooSmall_ReturnsZeroContracts()
        {
            // $5 risk, 20 tick stop, $5/tick = 0.05 contracts -> should be rounded down to zero
            int size = RiskCalculator.CalculatePositionSize(5, 20, 5);
            Assert.Equal(0, size);
        }
    }
}
