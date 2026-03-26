using Moq;
using TradingPlatform.BusinessLayer;

namespace AutoSizeStrategy.Test
{
    public class RiskCalculatorTests
    {
        #region CalculatePositionSize
        [Theory]
        // positionRisk, stopDistanceTicks, tickValue, expectedSize
        [InlineData(500, 20, 5, 5)]
        [InlineData(500, 30, 5, 3)]
        [InlineData(100, 40, 0.5, 5)]
        [InlineData(300, 25, 1, 12)]
        public void CalculatePositionSize3_Scenarios_CalculateCorrectly(
            double positionRisk,
            double stopDistanceTicks,
            double tickValue,
            double expectedSize
        )
        {
            int size = RiskCalculator.CalculatePositionSize(
                positionRisk,
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
        // STOP AT ENTRY: Risk $100. Stop 0pts ES.
        // Result: Not allowed in UI, should return 0 -> cancel request
        [InlineData(100, 4100, 4100, 0.25, 5, 0)]
        public void CalculatePositionSize5_Scenarios_ReturnCorrectSize(
            double positionRisk,
            double entryPrice,
            double exitPrice,
            double tickSize,
            double tickValue,
            double expectedSize
        )
        {
            int size = RiskCalculator.CalculatePositionSize(
                positionRisk,
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
            double positionRisk,
            double stop,
            double tickVal
        )
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                RiskCalculator.CalculatePositionSize(positionRisk, stop, tickVal)
            );
        }

        [Theory]
        [InlineData(1000, 6000, 6005, 0, 50)] // zero tick size
        [InlineData(1000, 6000, 6005, -0.25, 50)] // negative tick size
        public void CalculatePositionSize5_InvalidInputs_ThrowsArgumentException(
            double positionRisk,
            double entryPrice,
            double stopPrice,
            double tickSize,
            double tickValue
        )
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                RiskCalculator.CalculatePositionSize(
                    positionRisk,
                    entryPrice,
                    stopPrice,
                    tickSize,
                    tickValue
                )
            );
        }

        [Theory]
        [InlineData(double.NaN, 20, 5)]
        [InlineData(double.PositiveInfinity, 20, 5)]
        [InlineData(500, double.NaN, 5)]
        [InlineData(500, 20, double.NegativeInfinity)]
        public void CalculatePositionSize3_NonFiniteInputs_ThrowsException(
            double positionRisk,
            double stopTicks,
            double tickVal
        )
        {
            // These should trigger MathUtil.ValidateFinite
            Assert.ThrowsAny<ArgumentException>(() =>
                RiskCalculator.CalculatePositionSize(positionRisk, stopTicks, tickVal)
            );
        }

        [Theory]
        [InlineData(double.NaN, 5000, 4990, 0.25, 5)]
        [InlineData(500, double.PositiveInfinity, 4990, 0.25, 5)]
        [InlineData(500, 5000, double.NaN, 0.25, 5)]
        [InlineData(500, 5000, 4990, double.NegativeInfinity, 5)]
        [InlineData(500, 5000, 4990, 0.25, double.PositiveInfinity)]
        public void CalculatePositionSize5_NonFiniteInputs_ThrowsException(
            double risk,
            double entry,
            double stop,
            double tickSize,
            double tickVal
        )
        {
            Assert.ThrowsAny<ArgumentException>(() =>
                RiskCalculator.CalculatePositionSize(risk, entry, stop, tickSize, tickVal)
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

        #region GetStopDistanceTicks

        [Fact]
        public void GetStopDistanceTicks_AbsolutePriceMeasurement_ReturnsCorrect()
        {
            var slTpHolder = SlTpHolder.CreateSL(5995/*,  PriceMeasurement.Absolute */);
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
        [InlineData(6000, 6005, double.NegativeInfinity)] // -∞ tick size
        [InlineData(6000, 6005, double.PositiveInfinity)] // ∞ tick size
        [InlineData(6000, 6005, double.NaN)] // NaN tick size
        [InlineData(6000, double.NegativeInfinity, 0.25)] // -∞ tick size
        [InlineData(6000, double.PositiveInfinity, 0.25)] // ∞ stop price
        [InlineData(6000, double.NaN, 0.25)] // NaN stop price
        public void GetStopDistanceTicks_InvalidInputs_ThrowsArgumentException(
            double entryPrice,
            double stopPrice,
            double tickSize
        )
        {
            var slTpHolder = SlTpHolder.CreateSL(stopPrice/*, PriceMeasurement.Absolute*/);
            Assert.ThrowsAny<ArgumentException>(() =>
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

        [Theory]
        [InlineData(0.25, double.PositiveInfinity)]
        [InlineData(0.25, double.NegativeInfinity)]
        [InlineData(0.25, double.NaN)]
        [InlineData(double.PositiveInfinity, 25_000)]
        [InlineData(double.NegativeInfinity, 25_000)]
        [InlineData(double.NaN, 25_000)]
        public void GetStopDistanceTicks_NonFiniteEntryPrice_ThrowsArgumentException(
            double tickSize,
            double entryPrice
        )
        {
            var slTpHolder = SlTpHolder.CreateSL(10, PriceMeasurement.Offset);

            Assert.ThrowsAny<Exception>(() =>
                RiskCalculator.GetStopDistanceTicks(slTpHolder, tickSize, entryPrice)
            );
        }

        #endregion

        #region CalculatePositionRisk

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
        public void CalculatePositionRisk_Static_Scenarios(
            double balance,
            double riskPercent,
            double expectedRisk
        )
        {
            var account = CreateAccount(balance);

            double positionRisk = RiskCalculator.CalculatePositionRisk(
                account,
                riskPercent: riskPercent,
                DrawdownMode.Static,
                out string reason
            );

            Assert.Equal(expectedRisk, positionRisk, precision: 4);
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
        public void CalculatePositionRisk_Intraday_Scenarios(
            double balance,
            double liquidateThreshold,
            double expectedRisk
        )
        {
            var account = CreateAccount(
                balance,
                new Dictionary<string, string>
                {
                    { "AutoLiquidateThresholdCurrentValue", $"{liquidateThreshold}" },
                }
            );

            double positionRisk = RiskCalculator.CalculatePositionRisk(
                account,
                riskPercent: 10.0,
                DrawdownMode.Intraday,
                out string reason
            );

            Assert.Equal(expectedRisk, positionRisk, precision: 4);
            Assert.Contains("OK", reason);
        }

        [Theory]
        [InlineData("inf")]
        [InlineData("-inf")]
        [InlineData("Infinity")]
        [InlineData("-Infinity")]
        [InlineData("NaN")]
        public void CalculatePositionRisk_InfiniteLiquidateThreshold_Throws(
            string liquidateThreshold
        )
        {
            var account = CreateAccount(
                150_000,
                new Dictionary<string, string>
                {
                    { "AutoLiquidateThresholdCurrentValue", liquidateThreshold },
                }
            );

            Assert.Throws<ArgumentException>(() =>
                RiskCalculator.CalculatePositionRisk(
                    account,
                    riskPercent: 10.0,
                    DrawdownMode.Intraday,
                    out _
                )
            );
        }

        [Theory]
        [InlineData(151_438.25, 147_966.0, 347.225)]
        [InlineData(150_000, 145_500, 450.0)]
        [InlineData(152_000, 147_500, 450.0)]
        [InlineData(146_000, 145_500, 50.0)]
        [InlineData(145_510, 145_500, 1.0)]
        public void CalculatePositionRisk_EndOfDay_WithOverride_CalculatesCorrectly(
            double balance,
            double minBalanceOverride,
            double expectedRisk
        )
        {
            var account = CreateAccount(balance);

            double positionRisk = RiskCalculator.CalculatePositionRisk(
                account,
                riskPercent: 10.0,
                DrawdownMode.EndOfDay,
                out _,
                minAccountBalanceOverride: minBalanceOverride
            );

            Assert.Equal(expectedRisk, positionRisk, precision: 2);
        }

        [Fact]
        public void CalculatePositionRisk_EOD_NoOverride_ReturnsZero()
        {
            var account = CreateAccount(150_000);

            double positionRisk = RiskCalculator.CalculatePositionRisk(
                account,
                riskPercent: 10.0,
                DrawdownMode.EndOfDay,
                out string reason,
                minAccountBalanceOverride: 0.0
            );

            Assert.Equal(0, positionRisk);
            Assert.Contains("requires", reason);
        }

        [Fact]
        public void CalculatePositionRisk_FuzzTest_DoesNotCrash()
        {
            var rnd = new Random(123);
            for (int i = 0; i < 1000; i++)
            {
                double balance = rnd.Next(140_000, 160_000);
                double minBalance = rnd.Next(140_000, 150_000);

                var account = CreateAccount(balance);

                var risk = RiskCalculator.CalculatePositionRisk(
                    account,
                    10,
                    DrawdownMode.EndOfDay,
                    out _,
                    minAccountBalanceOverride: minBalance
                );

                Assert.True(risk >= 0, $"Failed on iteration {i}: Risk was negative ({risk})");
            }
        }

        [Fact]
        public void CalculatePositionRisk_NullAccount_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                RiskCalculator.CalculatePositionRisk(
                    account: null,
                    riskPercent: 1.0,
                    DrawdownMode.Static,
                    out _
                )
            );
        }

        [Theory]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        [InlineData(double.NaN)]
        [InlineData(0.0)]
        [InlineData(-5.0)]
        public void CalculatePositionRisk_InvalidRiskPercent_ThrowsArgumentException(double percent)
        {
            var account = CreateAccount(150_000);
            Assert.ThrowsAny<ArgumentException>(() =>
                RiskCalculator.CalculatePositionRisk(
                    account,
                    riskPercent: percent,
                    DrawdownMode.Static,
                    out _
                )
            );
        }

        [Fact]
        public void CalculatePositionRisk_RiskPercent_Exceeds100_ThrowsArgumentException()
        {
            var account = CreateAccount(150_000);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                RiskCalculator.CalculatePositionRisk(
                    account,
                    riskPercent: 150.0,
                    DrawdownMode.Static,
                    out _
                )
            );
        }

        #endregion

        #region GetAvailableDrawdown

        [Theory]
        [InlineData(150_000, 150_000)]
        [InlineData(10_000, 10_000)]
        [InlineData(500, 500)]
        public void Static_ReturnsFullBalance(double balance, double expectedDrawdown)
        {
            var account = CreateAccount(balance);

            double result = RiskCalculator.GetAvailableDrawdown(
                account,
                DrawdownMode.Static,
                out string reason
            );

            Assert.Equal(expectedDrawdown, result);
            Assert.Contains("OK", reason);
        }

        [Theory]
        [InlineData(150_000, 145_500, 4_500)]
        [InlineData(152_000, 147_500, 4_500)]
        [InlineData(146_000, 145_500, 500)]
        [InlineData(145_510, 145_500, 10)]
        public void Intraday_ReturnsBalanceMinusThreshold(
            double balance,
            double threshold,
            double expectedDrawdown
        )
        {
            var account = CreateAccount(
                balance,
                new Dictionary<string, string>
                {
                    { "AutoLiquidateThresholdCurrentValue", $"{threshold}" },
                }
            );

            double result = RiskCalculator.GetAvailableDrawdown(
                account,
                DrawdownMode.Intraday,
                out _
            );

            Assert.Equal(expectedDrawdown, result);
        }

        [Fact]
        public void Intraday_MissingThreshold_ReturnsZero()
        {
            var account = CreateAccount(150_000, new Dictionary<string, string>());

            double result = RiskCalculator.GetAvailableDrawdown(
                account,
                DrawdownMode.Intraday,
                out string reason
            );

            Assert.Equal(0, result);
            Assert.Contains("Missing", reason);
        }

        [Fact]
        public void EOD_NoOverride_ReturnsZero()
        {
            var account = CreateAccount(150_000);

            double result = RiskCalculator.GetAvailableDrawdown(
                account,
                DrawdownMode.EndOfDay,
                out string reason
            );

            Assert.Equal(0, result);
            Assert.Contains("requires", reason);
        }

        [Theory]
        [InlineData(151_438.25, 147_966.0, 3472.25)]
        [InlineData(150_000, 145_500, 4_500)]
        public void EOD_WithOverride_ReturnsBalanceMinusOverride(
            double balance,
            double minOverride,
            double expectedDrawdown
        )
        {
            var account = CreateAccount(balance);

            double result = RiskCalculator.GetAvailableDrawdown(
                account,
                DrawdownMode.EndOfDay,
                out _,
                minAccountBalanceOverride: minOverride
            );

            Assert.Equal(expectedDrawdown, result, precision: 2);
        }

        [Fact]
        public void Override_TakesPrecedence_OverMode()
        {
            // Even for Static mode, override should be used when > 0
            var account = CreateAccount(150_000);

            double result = RiskCalculator.GetAvailableDrawdown(
                account,
                DrawdownMode.Static,
                out _,
                minAccountBalanceOverride: 145_000
            );

            Assert.Equal(5_000, result);
        }

        [Fact]
        public void NegativeDrawdown_ReturnsZero()
        {
            // Balance below threshold
            var account = CreateAccount(
                144_000,
                new Dictionary<string, string>
                {
                    { "AutoLiquidateThresholdCurrentValue", "145500" },
                }
            );

            double result = RiskCalculator.GetAvailableDrawdown(
                account,
                DrawdownMode.Intraday,
                out string reason
            );

            Assert.Equal(0, result);
            Assert.Contains("zero or negative", reason);
        }

        [Fact]
        public void NullAccount_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                RiskCalculator.GetAvailableDrawdown(null, DrawdownMode.Static, out _)
            );
        }

        [Fact]
        public void CalculatePositionRisk_StillWorksAfterRefactor()
        {
            // Verify the existing API produces identical results after extraction
            var account = CreateAccount(
                150_000,
                new Dictionary<string, string>
                {
                    { "AutoLiquidateThresholdCurrentValue", "145500" },
                }
            );

            double positionRisk = RiskCalculator.CalculatePositionRisk(
                account,
                10.0,
                DrawdownMode.Intraday,
                out string reason
            );

            // Available = 4500. Risk 10% = 450.
            Assert.Equal(450, positionRisk, precision: 4);
            Assert.Contains("OK", reason);
        }

        #endregion

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
    }
}
