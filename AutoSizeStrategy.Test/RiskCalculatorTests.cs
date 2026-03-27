using Moq;
using TradingPlatform.BusinessLayer;

namespace AutoSizeStrategy.Test
{
    public class RiskCalculatorTests
    {
        #region Sizing and Cost Math

        [Theory]
        [InlineData(500, 100, 5)]
        [InlineData(500, 150, 3)] // 500/150 = 3.33 -> 3
        [InlineData(100, 20, 5)]
        [InlineData(300, 25, 12)]
        [InlineData(50, 100, 0)] // Risk too small for 1 contract
        public void CalculatePositionSize_Scenarios_CalculateCorrectly(
            double positionRisk,
            double costPerContract,
            double expectedSize
        )
        {
            int size = RiskCalculator.CalculatePositionSize(positionRisk, costPerContract);
            Assert.Equal(expectedSize, size);
        }

        [Theory]
        // STANDARD (ES-like): Stop 5pts (20 ticks). Tick Value $5. No slip/comm. -> 20 * 5 = 100
        [InlineData(20, 5, 0, 0, 100)]
        // MICROS (MES-like): Stop 10pts (40 ticks). Tick Value $1.25. Slip 1 tick. Comm $0.50. -> (40+1)*1.25 + 0.50 = 51.75
        [InlineData(40, 1.25, 1, 0.50, 51.75)]
        // NASDAQ (NQ-like): Stop 20pts (80 ticks). Tick Value $5. Slip 2 ticks. Comm $5. -> (80+2)*5 + 5 = 415
        [InlineData(80, 5, 2, 5, 415)]
        // CRUDE OIL (CL-like): Stop 30 ticks. Tick Value $10. Slip 0. Comm $3. -> 30*10 + 3 = 303
        [InlineData(30, 10, 0, 3, 303)]
        public void CalculateCostPerContract_Scenarios_ReturnCorrectCost(
            double stopDistanceTicks,
            double tickValue,
            double slippageTicks,
            double roundTripCommission,
            double expectedCost
        )
        {
            double cost = RiskCalculator.CalculateCostPerContract(
                stopDistanceTicks,
                tickValue,
                slippageTicks,
                roundTripCommission
            );

            Assert.Equal(expectedCost, cost);
        }

        [Theory]
        [InlineData(0, 5, 0, 0)] // zero stop
        [InlineData(20, 0, 0, 0)] // zero tick value
        [InlineData(20, 5, -1, 0)] // negative slip
        [InlineData(20, 5, 0, -1)] // negative comm
        public void CalculateCostPerContract_InvalidInputs_ThrowsArgumentException(
            double stop, double tick, double slip, double comm)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                RiskCalculator.CalculateCostPerContract(stop, tick, slip, comm)
            );
        }

        [Theory]
        [InlineData(double.NaN, 5, 0, 0)]
        [InlineData(20, double.PositiveInfinity, 0, 0)]
        [InlineData(20, 5, double.NaN, 0)]
        [InlineData(20, 5, 0, double.NegativeInfinity)]
        public void CalculateCostPerContract_NonFiniteInputs_ThrowsException(
            double stop, double tick, double slip, double comm)
        {
            Assert.ThrowsAny<ArgumentException>(() =>
                RiskCalculator.CalculateCostPerContract(stop, tick, slip, comm)
            );
        }

        [Theory]
        [InlineData(double.NaN, 100)]
        [InlineData(500, double.NaN)]
        [InlineData(double.PositiveInfinity, 100)]
        [InlineData(500, double.PositiveInfinity)]
        public void CalculatePositionSize_NonFiniteInputs_ThrowsException(
            double risk, double cost)
        {
            Assert.ThrowsAny<ArgumentException>(() =>
                RiskCalculator.CalculatePositionSize(risk, cost)
            );
        }

        #endregion

        #region GetStopDistanceTicks

        [Fact]
        public void GetStopDistanceTicks_AbsolutePriceMeasurement_ReturnsCorrect()
        {
            var slTpHolder = SlTpHolder.CreateSL(5995 /*,  PriceMeasurement.Absolute */);
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
            var slTpHolder = SlTpHolder.CreateSL(stopPrice /*, PriceMeasurement.Absolute*/);
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
