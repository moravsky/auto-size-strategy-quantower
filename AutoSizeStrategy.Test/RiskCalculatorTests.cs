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
        public void CalculatePositionSize_Overload_ReturnsCorrectSize()
        {
            // $500 risk, 20 tick stop, $5/tick = 5 contracts
            int size = RiskCalculator.CalculatePositionSize(500, 5005, 5000, 0.25, 5);
            Assert.Equal(5, size);
        }

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
        [InlineData(500, 0, 5)] // zero stop
        [InlineData(500, 20, 0)] // zero tick value
        public void CalculatePositionSize3_InvalidInputs_ThrowsArgumentException(
            double riskCapital,
            double stop,
            double tickVal
        )
        {
            Assert.Throws<ArgumentException>(() =>
                RiskCalculator.CalculatePositionSize(riskCapital, stop, tickVal)
            );
        }

        [Theory]
        [InlineData(1000, 6000, 6005, 0, 50)] // zero tick size
        [InlineData(1000, 6000, 6005, -0.25, 50)] // negative tick size
        public void CalculatePositionSize5_InvalidInputs_ThrowsArgumentException(
            double riskCapital,
            double entryPrice,
            double stopPrice,
            double tickSize,
            double tickValue
        )
        {
            Assert.Throws<ArgumentException>(() =>
                RiskCalculator.CalculatePositionSize(
                    riskCapital,
                    entryPrice,
                    stopPrice,
                    tickSize,
                    tickValue
                )
            );
        }

        [Fact]
        public void GetStopDistanceTicks_AbsolutePriceMeasurement_ReturnsCorrect()
        {
            var slTpHolder = SlTpHolder.CreateSL(5995, PriceMeasurement.Absolute);
            double stopDistanceTicks = RiskCalculator.GetStopDistanceTicks(slTpHolder, 0.25, 6000);
            Assert.Equal(20, stopDistanceTicks);
        }

        [Fact]
        public void GetStopDistanceTicks_OffsetPriceMeasurement_ReturnsCorrect()
        {
            var slTpHolder = SlTpHolder.CreateSL(32, PriceMeasurement.Offset);
            double stopDistanceTicks = RiskCalculator.GetStopDistanceTicks(slTpHolder, 0.25, 6000);
            Assert.Equal(32, stopDistanceTicks);
        }

        [Theory]
        [InlineData(6000, 6005, 0)] // zero tick size
        [InlineData(6000, 6005, -0.25)] // negative tick size
        public void GetStopDistanceTicks_InvalidInputs_ThrowsArgumentException(
            double entryPrice,
            double stopPrice,
            double tickSize
        )
        {
            var slTpHolder = SlTpHolder.CreateSL(stopPrice, PriceMeasurement.Absolute);
            Assert.Throws<ArgumentException>(() =>
                RiskCalculator.GetStopDistanceTicks(slTpHolder, tickSize, entryPrice)
            );
        }

        [Fact]
        public void GetStopDistanceTicks_InvalidPriceMeasurement_ThrowsArgumentException()
        {
            var slTpHolder = SlTpHolder.CreateSL(5995, (PriceMeasurement)12314L);
            Assert.Throws<ArgumentException>(() =>
                RiskCalculator.GetStopDistanceTicks(slTpHolder, 0.25, 6000)
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
        private static IAccount CreateAccount(
            double balance,
            Dictionary<string, string>? additionalInfo = null
        )
        {
            var mock = new Mock<IAccount>();
            mock.SetupGet(a => a.Balance).Returns(balance);
            if (additionalInfo != null)
            {
                mock.SetupGet(a => a.AdditionalInfo).Returns(additionalInfo);
            }
            return mock.Object;
        }

        [Fact]
        public void CalculateRiskCapital_StaticMode_ReturnsCorrectCapital()
        {
            var account = CreateAccount(150_000);
            string calculationReason = "";
            double riskCapital = RiskCalculator.CalculateRiskCapital(
                account,
                riskPercent: 1.0,
                DrawdownMode.Static,
                out calculationReason
            );

            Assert.Equal(1500.0, riskCapital, precision: 4);
            Assert.Contains("OK", calculationReason);
        }

        [Fact]
        public void CalculateRiskCapital_IntradayMode_WithStub_ReturnsExpectedCapital()
        {
            var account = CreateAccount(
                150_000,
                new Dictionary<string, string>
                {
                    { "AutoLiquidateThresholdCurrentValue", "145500" },
                }
            );
            string calculationReason = "";
            double riskCapital = RiskCalculator.CalculateRiskCapital(
                account,
                riskPercent: 10.0,
                DrawdownMode.Intraday,
                out calculationReason
            );

            Assert.Equal(450.0, riskCapital, precision: 4);
            Assert.Contains("OK", calculationReason);
        }

        [Fact]
        public void CalculateRiskCapital_EndOfDayMode_WithStub_ReturnsExpectedCapital()
        {
            var account = CreateAccount(
                150_000,
                new Dictionary<string, string>
                {
                    { "AutoLiquidateThreshold", "4500" },
                    { "MinAccountBalance", "145500" },
                    { "NetPnL", "0" },
                }
            );
            string calculationReason = "";
            double riskCapital = RiskCalculator.CalculateRiskCapital(
                account,
                riskPercent: 10.0,
                DrawdownMode.EndOfDay,
                out calculationReason
            );

            /* Same stub as above – keep for future consistency test. */
            Assert.Equal(450.0, riskCapital, precision: 4);
            Assert.Contains("OK", calculationReason);
        }

        [Fact]
        public void CalculateRiskCapital_NullAccount_ThrowsArgumentNullException()
        {
            string calculationReason = "";
            Assert.Throws<ArgumentNullException>(() =>
                RiskCalculator.CalculateRiskCapital(
                    account: null,
                    riskPercent: 1.0,
                    DrawdownMode.Static,
                    out calculationReason
                )
            );
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-5.0)]
        public void CalculateRiskCapital_InvalidRiskPercent_ThrowsArgumentException(double percent)
        {
            var account = CreateAccount(150_000);
            string calculationReason = "";
            Assert.Throws<ArgumentException>(() =>
                RiskCalculator.CalculateRiskCapital(
                    account,
                    riskPercent: percent,
                    DrawdownMode.Static,
                    out calculationReason
                )
            );
        }

        [Fact]
        public void CalculateRiskCapital_RiskPercent_Exceeds100_ThrowsArgumentException()
        {
            var account = CreateAccount(150_000);
            string calculationReason = "";
            Assert.Throws<ArgumentException>(() =>
                RiskCalculator.CalculateRiskCapital(
                    account,
                    riskPercent: 150.0,
                    DrawdownMode.Static,
                    out calculationReason
                )
            );
        }

        #endregion
    }
}
