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
        [Theory]
        // riskCapital, stopDistanceTicks, tickValue, expectedSize
        [InlineData(500, 20, 5, 5)]
        [InlineData(500, 30, 5, 3)]
        [InlineData(100, 40, 0.5, 5)]
        [InlineData(300, 25, 1, 12)]
        public void CalculatePositionSize3_Scenarios_CalculatesCorrectly(
            double riskCapital,
            double stopDistanceTicks,
            double tickValue,
            double expectedSize
        )
        {
            int size = RiskCalculator.CalculatePositionSize(
                riskCapital,
                stopDistanceTicks,
                tickValue
            );
            Assert.Equal(expectedSize, size);
        }

        [Theory]
        // STANDARD (ES-like): Risk $500. Stop 5pts (20 ticks).
        // Risk/Contract: 20 ticks * $5/tick = $100.
        // Result: 500 / 100 = 5.
        [InlineData(500, 5005, 5000, 0.25, 5, 5)]
        // ROUNDING DOWN: Risk $590. Stop 5pts ($100 risk).
        // Result: 590 / 100 = 5.9 -> Floors to 5.
        [InlineData(590, 5005, 5000, 0.25, 5, 5)]
        // MICROS (MES-like): Risk $200. Stop 10pts (40 ticks).
        // Tick Value $1.25.
        // Risk/Contract: 40 * 1.25 = $50.
        // Result: 200 / 50 = 4.
        [InlineData(200, 4100, 4090, 0.25, 1.25, 4)]
        // NASDAQ (NQ-like): Risk $1,000. Stop 20pts (80 ticks).
        // Tick Value $5.
        // Risk/Contract: 80 * 5 = $400.
        // Result: 1000 / 400 = 2.5 -> Floors to 2.
        [InlineData(1000, 15020, 15000, 0.25, 5, 2)]
        // CRUDE OIL (CL-like): Risk $800. Stop 30 ticks ($0.30 price).
        // Tick Size 0.01. Tick Value $10.
        // Risk/Contract: 30 * 10 = $300.
        // Result: 800 / 300 = 2.66 -> Floors to 2.
        [InlineData(800, 75.30, 75.00, 0.01, 10, 2)]
        // GOLD (GC-like): Risk $500. Stop 20 ticks ($2.00 price).
        // Tick Size 0.1. Tick Value $10.
        // Risk/Contract: 20 * 10 = $200.
        // Result: 500 / 200 = 2.5 -> Floors to 2.
        [InlineData(500, 2002.0, 2000.0, 0.1, 10, 2)]
        // CURRENCY (6E-like): Risk $500. Stop 20 ticks (0.0010 price).
        // Tick Size 0.00005. Tick Value $6.25.
        // Risk/Contract: 20 * 6.25 = $125.
        // Result: 500 / 125 = 4.
        [InlineData(500, 1.1010, 1.1000, 0.00005, 6.25, 4)]
        // UNDERSIZED: Risk $50. Stop 5pts ES ($100 risk).
        // Result: 50 / 100 = 0.5 -> Floors to 0.
        [InlineData(50, 4105, 4100, 0.25, 5, 0)]
        // BARELY ENOUGH: Risk $100. Stop 5pts ES ($100 risk).
        // Result: Exact match = 1.
        [InlineData(100, 4105, 4100, 0.25, 5, 1)]
        public void CalculatePositionSize_Overload_ReturnsCorrectSize(
            double riskCapital,
            double entryPrice,
            double exitPrice,
            double tickSize,
            double tickValue,
            double expectedSize
        )
        {
            int size = RiskCalculator.CalculatePositionSize(
                riskCapital,
                entryPrice,
                exitPrice,
                tickSize,
                tickValue
            );

            Assert.Equal(expectedSize, size);
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

        [Theory]
        // STANDARD ACCOUNT
        // Balance 10,000. Risk 2% = 200.
        [InlineData(10_000, 2.0, 200.0)]
        // TINY ACCOUNT
        // Balance 500. Risk 10% = 50.
        [InlineData(500, 10.0, 50.0)]
        // DREAM COME TRUE
        // Balance 1,000,000. Risk 0.5% = 5,000.
        [InlineData(1_000_000, 0.5, 5_000.0)]
        public void CalculateRiskCapital_Static_Scenarios(
            double balance,
            double riskPercent,
            double expectedRisk
        )
        {
            var account = CreateAccount(balance);

            double riskCapital = RiskCalculator.CalculateRiskCapital(
                account,
                riskPercent: riskPercent,
                DrawdownMode.Static,
                out string reason
            );

            Assert.Equal(expectedRisk, riskCapital, precision: 4);
            Assert.Contains("OK", reason);
        }

        [Theory]
        // FRESH START
        // Balance 150k. Trailing Stop is at 145.5k.
        // Room: 4,500. Risk 10% = 450.
        [InlineData(150_000, 145_500, 450.0)]
        // #WINNING (Trailing Stop moved up)
        // Balance 152k. Trailing Stop followed up to 147.5k.
        // Room: 4,500. Risk 10% = 450.
        [InlineData(152_000, 147_500, 450.0)]
        // LOSING (Trailing Stop stayed put)
        // Balance 146k. Trailing Stop stuck at 145.5k.
        // Room: 500. Risk 10% = 50.
        [InlineData(146_000, 145_500, 50.0)]
        // LIFE SUPPORT
        // Balance 145,510. Trailing Stop at 145,500.
        // Room: 10. Risk 10% = 1.
        [InlineData(145_510, 145_500, 1.0)]
        public void CalculateRiskCapital_Intraday_Scenarios(
            double balance,
            double liquidateThreshold,
            double expectedRisk
        )
        {
            var account = CreateAccount(
                balance,
                new Dictionary<string, string>
                {
                    { "AutoLiquidateThresholdCurrentValue", liquidateThreshold.ToString() },
                }
            );

            double riskCapital = RiskCalculator.CalculateRiskCapital(
                account,
                riskPercent: 10.0,
                DrawdownMode.Intraday,
                out string reason
            );

            Assert.Equal(expectedRisk, riskCapital, precision: 4);
            Assert.Contains("OK", reason);
        }

        [Theory]
        // FRESH START (Happy Path)
        // Balance 150k. PnL 0. Buffer 4500. Risk 10%.
        // Result: Min(150k-145.5k, 4500+0) = 4500. Risk = 450.
        [InlineData(150_000, 0, 450.0)]
        // #WINNING (Buffer Expands)
        // We made $1000. Balance is up to 151k.
        // Available = DrawdownSize(4500) + PnL(1000) = 5500.
        // Hard Floor check: 151k - 145.5k = 5500.
        // Result: 5500. Risk = 550.
        [InlineData(151_000, 1000, 550.0)]
        // LOSING DAY (Buffer Contracts)
        // We lost $1000. Balance is down to 149k.
        // Available = DrawdownSize(4500) + PnL(-1000) = 3500.
        // Hard Floor check: 149k - 145.5k = 3500.
        // Result: 3500. Risk = 350.
        [InlineData(149_000, -1000, 350.0)]
        // THE "HARD FLOOR" TRAP (Yesterday was bad)
        // Balance is 146,000 (we lost 4k yesterday). Today PnL is 0.
        // Buffer says: 4500 + 0 = 4500 allowed? NO!
        // Hard Floor says: 146,000 - 145,500 = only 500 allowed.
        // Logic must choose the smaller (500). Risk = 50.
        [InlineData(146_000, 0, 50.0)]
        // RECOVERY MODE (Winning after a bad drawdown)
        // Balance 146,500. We made $500 today.
        // Hard Floor: 146.5k - 145.5k = 1000.
        // Buffer: 4500 + 500 = 5000.
        // Restult: Limited by Hard Floor (1000). Risk = 100.
        [InlineData(146_500, 500, 100.0)]
        public void CalculateRiskCapital_EndOfDay_Scenarios(
            double balance,
            double netPnl,
            double expectedRisk
        )
        {
            var account = CreateAccount(
                balance,
                new Dictionary<string, string>
                {
                    { "AutoLiquidateThreshold", "4500" },
                    { "MinAccountBalance", "145500" },
                    { "NetPnL", netPnl.ToString() },
                }
            );

            double riskCapital = RiskCalculator.CalculateRiskCapital(
                account,
                riskPercent: 10.0,
                DrawdownMode.EndOfDay,
                out string reason
            );

            Assert.Equal(expectedRisk, riskCapital, precision: 4);
            Assert.Contains("OK", reason);
        }

        [Fact]
        public void CalculateRiskCapital_EOD_MissingPnL_DefaultsToZero()
        {
            // Account has Balance & Thresholds, but NO NetPnL
            var account = CreateAccount(
                150_000,
                new Dictionary<string, string>
                {
                    { "AutoLiquidateThreshold", "4500" },
                    { "MinAccountBalance", "145500" },
                    // "NetPnL" is missing
                }
            );

            double riskCapital = RiskCalculator.CalculateRiskCapital(
                account,
                riskPercent: 10.0,
                DrawdownMode.EndOfDay,
                out string reason
            );

            // Should behave like PnL is 0.
            // Risk = 10% of 4500 = 450.
            Assert.Equal(450.0, riskCapital);
            Assert.Contains("OK", reason);
        }

        [Fact]
        public void CalculateRiskCapital_FuzzTest_DoesNotCrash()
        {
            var rnd = new Random(123);
            for (int i = 0; i < 1000; i++)
            {
                // Generate random (sometimes wild) account states
                double balance = rnd.Next(140_000, 160_000);
                double pnl = rnd.Next(-10_000, 10_000);

                var account = CreateAccount(
                    balance,
                    new Dictionary<string, string>
                    {
                        { "AutoLiquidateThreshold", "4500" },
                        { "MinAccountBalance", "145500" },
                        { "NetPnL", pnl.ToString() },
                    }
                );

                // We just want to ensure this never throws an exception
                // and returns a non-negative number
                var risk = RiskCalculator.CalculateRiskCapital(
                    account,
                    10,
                    DrawdownMode.EndOfDay,
                    out _
                );

                Assert.True(risk >= 0, $"Failed on iteration {i}: Risk was negative ({risk})");
            }
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
