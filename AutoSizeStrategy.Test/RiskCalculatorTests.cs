// AutoSizeStrategy.Test\RiskCalculatorTests.cs
using System;
using AutoSizeStrategy;
using Moq;
using TradingPlatform.BusinessLayer;
using Xunit;

namespace AutoSizeStrategy.Test
{
    /// <summary>
    /// Unit tests for <see cref="RiskCalculator"/>.
    /// </summary>
    public class RiskCalculatorTests
    {
        #region CalculateRiskCapital
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

        #endregion

        #region CalculateRiskCapital
        private static IAccount CreateAccount(double balance)
        {
            var mock = new Mock<IAccount>();
            mock.SetupGet(a => a.Balance).Returns(balance);
            return mock.Object;
        }

        [Fact]
        public void CalculateRiskCapital_StaticMode_ReturnsCorrectCapital()
        {
            var account = CreateAccount(150_000);
            double riskCapital = RiskCalculator.CalculateRiskCapital(
                account,
                riskPercent: 1.0,
                DrawdownMode.Static
            );

            Assert.Equal(1500.0, riskCapital, precision: 4);
        }

        [Fact]
        public void CalculateRiskCapital_IntradayMode_WithStub_ReturnsExpectedCapital()
        {
            var account = CreateAccount(150_000);
            double riskCapital = RiskCalculator.CalculateRiskCapital(
                account,
                riskPercent: 10.0,
                DrawdownMode.Intraday
            );

            Assert.Equal(450.0, riskCapital, precision: 4);
        }

        [Fact]
        public void CalculateRiskCapital_EndOfDayMode_WithStub_ReturnsExpectedCapital()
        {
            var account = CreateAccount(150_000);
            double riskCapital = RiskCalculator.CalculateRiskCapital(
                account,
                riskPercent: 10.0,
                DrawdownMode.EndOfDay
            );

            /* Same stub as above – keep for future consistency test. */
            Assert.Equal(450.0, riskCapital, precision: 4);
        }

        [Fact]
        public void CalculateRiskCapital_NullAccount_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                RiskCalculator.CalculateRiskCapital(
                    account: null,
                    riskPercent: 1.0,
                    DrawdownMode.Static
                )
            );
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-5.0)]
        public void CalculateRiskCapital_InvalidRiskPercent_ThrowsArgumentException(double percent)
        {
            var account = CreateAccount(150_000);
            Assert.Throws<ArgumentException>(() =>
                RiskCalculator.CalculateRiskCapital(
                    account,
                    riskPercent: percent,
                    DrawdownMode.Static
                )
            );
        }

        [Fact]
        public void CalculateRiskCapital_RiskPercent_Exceeds100_ThrowsArgumentException()
        {
            var account = CreateAccount(150_000);
            Assert.Throws<ArgumentException>(() =>
                RiskCalculator.CalculateRiskCapital(
                    account,
                    riskPercent: 150.0,
                    DrawdownMode.Static
                )
            );
        }

        #endregion
    }
}
