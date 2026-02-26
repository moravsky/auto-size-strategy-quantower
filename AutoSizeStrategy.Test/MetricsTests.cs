using System;
using System.Collections.Generic;
using AutoSizeStrategy;
using Moq;
using TradingPlatform.BusinessLayer;
using Xunit;

namespace AutoSizeStrategy.Test
{
    public class MetricsTests
    {
        private readonly Mock<IStrategySettings> _settingsMock;
        private readonly Mock<IAccount> _accountMock;
        private readonly Mock<ISymbol> _symbolMock;

        public MetricsTests()
        {
            _settingsMock = new Mock<IStrategySettings>();
            _accountMock = new Mock<IAccount>();
            _symbolMock = new Mock<ISymbol>();

            // Starting Balance: $154,500
            // Threshold: $150,000
            // Initial Risk Budget: $4,500
            _accountMock.SetupGet(a => a.Id).Returns("TPPRO123456");
            _accountMock.SetupGet(a => a.Balance).Returns(150000);
            _accountMock
                .SetupGet(a => a.AdditionalInfo)
                .Returns(
                    new Dictionary<string, string>
                    {
                        { "AutoLiquidateThresholdCurrentValue", "145500" },
                    }
                );

            _settingsMock.SetupGet(s => s.CurrentAccount).Returns(_accountMock.Object);

            _symbolMock.SetupGet(s => s.Id).Returns("MNQ");
            _symbolMock.SetupGet(s => s.Last).Returns(18000);
            _symbolMock.Setup(s => s.GetTickCost(It.IsAny<double>())).Returns(0.50);

            _settingsMock.SetupGet(s => s.MinAccountBalanceOverride).Returns(0.0);
            _settingsMock.SetupGet(s => s.MinimumStopLossTicks).Returns(64); // 16 pts
            _settingsMock.SetupGet(s => s.RiskPercent).Returns(10.0);

            // Slip 1 tick ($0.50) + Comm ($0.25).
            // Loss = (64+1)*0.5 + (0.25*2) = 33.0.
            _settingsMock.SetupGet(s => s.AverageSlippageTicks).Returns(1.0);
            _settingsMock.SetupGet(s => s.CommissionMicro).Returns(0.25);

            // Trigger: 30% of Starting Balance (relative to failure)
            // 30% of $4,500 = $1,350
            _settingsMock.SetupGet(s => s.ClutchModeTriggerBalance).Returns(146850);
            // Clutch mode risk: 25%, 25%, 100% (YOLO)
            _settingsMock.SetupGet(s => s.ClutchModeRisk).Returns([0.25, 0.25, 1]);
        }

        [Theory]
        [InlineData(150000, 15)] // Full Start: 12 standard + 3 clutch = 15
        [InlineData(147500, 7)] // Mid-Drawdown ($2k buffer): Standard trades reduced
        public void GetAccountMetrics_StandardScenarios_ReturnCorrectMetrics(
            double balance,
            int expectedTrades
        )
        {
            _accountMock.SetupGet(a => a.Balance).Returns(balance);
            var metrics = new Metrics(_settingsMock.Object)
            {
                LastSymbol = _symbolMock.Object,
                LastStopDistanceTicks = 64,
            };

            var result = metrics.GetAccountMetrics();

            Assert.Equal(balance - 145500, result.DrawdownRemaining);
            Assert.Equal(expectedTrades, result.TradesToBust);
        }

        [Theory]
        [InlineData(146850, 1350, 3)] // Start of Clutch Mode (Trigger balance: $145.5k + 30% of $4.5k)
        [InlineData(146500, 1000, 2)] // Inside Clutch Mode, one trade lost
        [InlineData(146200, 700, 1)] // Deep in Clutch Mode, two trades lost
        public void GetAccountMetrics_ClutchModeBalances_ReturnCorrectMetrics(
            double balance,
            double expectedDrawdown,
            int expectedTradesToBust
        )
        {
            _accountMock.SetupGet(a => a.Balance).Returns(balance);

            var metrics = new Metrics(_settingsMock.Object)
            {
                LastSymbol = _symbolMock.Object,
                LastStopDistanceTicks = 64,
            };

            var result = metrics.GetAccountMetrics();

            Assert.Equal(expectedDrawdown, result.DrawdownRemaining);
            Assert.Equal(0, result.TradesToClutchMode);
            Assert.Equal(expectedTradesToBust, result.TradesToBust);
        }

        [Theory]
        [InlineData(150000, 15)] // Full Start: 12 standard + 3 clutch = 15
        [InlineData(147500, 7)] // Mid-Drawdown ($2k buffer): Standard trades reduced
        public void GetAccountMetrics_EODAccount_StandardScenarios_ReturnCorrectMetrics(
            double balance,
            int expectedTrades
        )
        {
            _accountMock.SetupGet(a => a.Balance).Returns(balance);
            _accountMock.SetupGet(a => a.Id).Returns("TPT123456");
            _settingsMock.SetupGet(s => s.MinAccountBalanceOverride).Returns(145500);
            var metrics = new Metrics(_settingsMock.Object)
            {
                LastSymbol = _symbolMock.Object,
                LastStopDistanceTicks = 64,
            };

            var result = metrics.GetAccountMetrics();

            Assert.Equal(balance - 145500, result.DrawdownRemaining);
            Assert.Equal(expectedTrades, result.TradesToBust);
        }

        [Theory]
        [InlineData(155000, 16)]
        [InlineData(152000, 7)]
        public void GetAccountMetrics_EODAccount_RaisedDradown_ReturnCorrectMetrics(
            double balance,
            int expectedTrades
        )
        {
            _accountMock.SetupGet(a => a.Balance).Returns(balance);
            _accountMock.SetupGet(a => a.Id).Returns("TPT123456");
            _settingsMock.SetupGet(s => s.MinAccountBalanceOverride).Returns(150000);
            _settingsMock.SetupGet(s => s.ClutchModeTriggerBalance).Returns(151350);
            var metrics = new Metrics(_settingsMock.Object)
            {
                LastSymbol = _symbolMock.Object,
                LastStopDistanceTicks = 64,
            };

            var result = metrics.GetAccountMetrics();

            Assert.Equal(balance - 150000, result.DrawdownRemaining);
            Assert.Equal(expectedTrades, result.TradesToBust);
        }

        [Theory]
        [InlineData(146850, 1350, 3)] // Start of Clutch Mode (Trigger balance: $145.5k + 30% of $4.5k)
        [InlineData(146500, 1000, 2)] // Inside Clutch Mode, one trade lost
        [InlineData(146200, 700, 1)] // Deep in Clutch Mode, two trades lost
        public void GetAccountMetrics_EODAccount_ClutchModeBalances_ReturnCorrectMetrics(
            double balance,
            double expectedDrawdown,
            int expectedTradesToBust
        )
        {
            _accountMock.SetupGet(a => a.Balance).Returns(balance);
            _accountMock.SetupGet(a => a.Id).Returns("TPT123456");
            _settingsMock.SetupGet(s => s.MinAccountBalanceOverride).Returns(145500);

            var metrics = new Metrics(_settingsMock.Object)
            {
                LastSymbol = _symbolMock.Object,
                LastStopDistanceTicks = 64,
            };

            var result = metrics.GetAccountMetrics();

            Assert.Equal(expectedDrawdown, result.DrawdownRemaining);
            Assert.Equal(0, result.TradesToClutchMode);
            Assert.Equal(expectedTradesToBust, result.TradesToBust);
        }

        [Theory]
        [InlineData(145500)]
        [InlineData(145400)]
        public void GetAccountMetrics_ZeroOrNegativeDrawdown_ReturnsZeros(double balance)
        {
            _accountMock.SetupGet(a => a.Balance).Returns(balance);

            var metrics = new Metrics(_settingsMock.Object);
            var result = metrics.GetAccountMetrics();

            Assert.Equal(0, result.DrawdownRemaining);
            Assert.Equal(0, result.TradesToBust);
            Assert.Equal(0, result.TradesToClutchMode);
        }

        [Theory]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        [InlineData(double.NaN)]
        public void GetAccountMetrics_NanTickCost_ReturnsNulls(double tickCost)
        {
            _accountMock.SetupGet(a => a.Balance).Returns(150000);
            _symbolMock.Setup(s => s.GetTickCost(It.IsAny<double>())).Returns(tickCost);
            var metrics = new Metrics(_settingsMock.Object)
            {
                LastSymbol = _symbolMock.Object,
                LastStopDistanceTicks = 64,
            };
            var result = metrics.GetAccountMetrics();

            Assert.Equal(4500, result.DrawdownRemaining);
            Assert.Null(result.TradesToBust);
            Assert.Null(result.TradesToClutchMode);
        }
    }
}
