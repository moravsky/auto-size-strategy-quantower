using Moq;
using TradingPlatform.BusinessLayer;

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

            _symbolMock.SetupGet(s => s.Id).Returns("MNQ@CME");
            _symbolMock.SetupGet(s => s.Name).Returns("MNQ");
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
            _settingsMock.SetupGet(s => s.ClutchModeBudget).Returns(1350.0);
            // Clutch mode risk: 25%, 25%, 100% (YOLO)
            _settingsMock.SetupGet(s => s.ClutchModeRisk).Returns([0.25, 0.25, 1]);
        }

        private Metrics CreateMetrics(
            ISymbol? symbol = null,
            double? stopDistanceTicks = null,
            List<IPosition>? positions = null,
            List<IOrder>? orders = null)
        {
            var tradingServiceMock = new Mock<ITradingService>();
            tradingServiceMock.Setup(ts => ts.GetPositions(It.IsAny<IAccount>()))
                .Returns(positions ?? Enumerable.Empty<IPosition>());
            tradingServiceMock.Setup(ts => ts.GetWorkingOrders(It.IsAny<IAccount>()))
                .Returns(orders ?? Enumerable.Empty<IOrder>());

            var metrics = new Metrics(_settingsMock.Object, tradingServiceMock.Object);

            if (symbol != null)
                metrics.LastSymbol = symbol;

            if (stopDistanceTicks.HasValue)
                metrics.LastStopDistanceTicks = stopDistanceTicks.Value;

            return metrics;
        }

        [Theory]
        [InlineData(150000, 16)] // Full Start: 13 standard + 3 clutch = 16
        [InlineData(147500, 8)] // Mid-Drawdown ($2k buffer): Standard trades reduced
        public void GetAccountMetrics_StandardScenarios_ReturnCorrectMetrics(
            double balance,
            int expectedTrades
        )
        {
            _accountMock.SetupGet(a => a.Balance).Returns(balance);
            var metrics = CreateMetrics(symbol: _symbolMock.Object, stopDistanceTicks: 64);

            var result = metrics.GetAccountMetrics();

            Assert.Equal(balance - 145500, result.RiskCapital);
            Assert.Equal(expectedTrades, result.TradesToBust);
        }

        [Theory]
        [InlineData(146850, 1350, 3)] // Start of Clutch Mode (Trigger balance: $145.5k + 30% of $4.5k)
        [InlineData(146500, 1000, 2)] // Inside Clutch Mode, one trade lost
        [InlineData(146200, 700, 1)] // Deep in Clutch Mode, two trades lost
        public void GetAccountMetrics_ClutchModeBalances_ReturnCorrectMetrics(
            double balance,
            double expectedRiskCapital,
            int expectedTradesToBust
        )
        {
            _accountMock.SetupGet(a => a.Balance).Returns(balance);
            var metrics = CreateMetrics(symbol: _symbolMock.Object, stopDistanceTicks: 64);

            var result = metrics.GetAccountMetrics();

            Assert.Equal(expectedRiskCapital, result.RiskCapital);
            Assert.Equal(0, result.TradesToClutchMode);
            Assert.Equal(expectedTradesToBust, result.TradesToBust);
        }

        [Theory]
        [InlineData(150000, 16)] // Full Start: 13 standard + 3 clutch = 16
        [InlineData(147500, 8)] // Mid-Drawdown ($2k buffer): Standard trades reduced
        public void GetAccountMetrics_EODAccount_StandardScenarios_ReturnCorrectMetrics(
            double balance,
            int expectedTrades
        )
        {
            _accountMock.SetupGet(a => a.Balance).Returns(balance);
            _accountMock.SetupGet(a => a.Id).Returns("TPT123456");
            _settingsMock.SetupGet(s => s.MinAccountBalanceOverride).Returns(145500);
            var metrics = CreateMetrics(symbol: _symbolMock.Object, stopDistanceTicks: 64);

            var result = metrics.GetAccountMetrics();

            Assert.Equal(balance - 145500, result.RiskCapital);
            Assert.Equal(expectedTrades, result.TradesToBust);
        }

        [Theory]
        [InlineData(155000, 17)]
        [InlineData(152000, 8)]
        public void GetAccountMetrics_EODAccount_RaisedDradown_ReturnCorrectMetrics(
            double balance,
            int expectedTrades
        )
        {
            _accountMock.SetupGet(a => a.Balance).Returns(balance);
            _accountMock.SetupGet(a => a.Id).Returns("TPT123456");
            _settingsMock.SetupGet(s => s.MinAccountBalanceOverride).Returns(150000);
            var metrics = CreateMetrics(symbol: _symbolMock.Object, stopDistanceTicks: 64);

            var result = metrics.GetAccountMetrics();

            Assert.Equal(balance - 150000, result.RiskCapital);
            Assert.Equal(expectedTrades, result.TradesToBust);
        }

        [Theory]
        [InlineData(146850, 1350, 3)] // Start of Clutch Mode (Trigger balance: $145.5k + 30% of $4.5k)
        [InlineData(146500, 1000, 2)] // Inside Clutch Mode, one trade lost
        [InlineData(146200, 700, 1)] // Deep in Clutch Mode, two trades lost
        public void GetAccountMetrics_EODAccount_ClutchModeBalances_ReturnCorrectMetrics(
            double balance,
            double expectedRiskCapital,
            int expectedTradesToBust
        )
        {
            _accountMock.SetupGet(a => a.Balance).Returns(balance);
            _accountMock.SetupGet(a => a.Id).Returns("TPT123456");
            _settingsMock.SetupGet(s => s.MinAccountBalanceOverride).Returns(145500);
            var metrics = CreateMetrics(symbol: _symbolMock.Object, stopDistanceTicks: 64);

            var result = metrics.GetAccountMetrics();

            Assert.Equal(expectedRiskCapital, result.RiskCapital);
            Assert.Equal(0, result.TradesToClutchMode);
            Assert.Equal(expectedTradesToBust, result.TradesToBust);
        }

        [Theory]
        [InlineData(145500)]
        [InlineData(145400)]
        public void GetAccountMetrics_ZeroOrNegativeDrawdown_ReturnsZeros(double balance)
        {
            _accountMock.SetupGet(a => a.Balance).Returns(balance);
            var metrics = CreateMetrics();

            var result = metrics.GetAccountMetrics();

            Assert.Equal(0, result.RiskCapital);
            Assert.Equal(0, result.TradesToBust);
            Assert.Equal(0, result.TradesToClutchMode);
            Assert.Equal(0, result.AbsoluteValueAtRisk);
            Assert.Equal(0, result.RelativeValueAtRiskPercent);
        }

        [Theory]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        [InlineData(double.NaN)]
        public void GetAccountMetrics_NanTickCost_ReturnsNulls(double tickCost)
        {
            _accountMock.SetupGet(a => a.Balance).Returns(150000);
            _symbolMock.Setup(s => s.GetTickCost(It.IsAny<double>())).Returns(tickCost);
            var metrics = CreateMetrics(symbol: _symbolMock.Object, stopDistanceTicks: 64);

            var result = metrics.GetAccountMetrics();

            Assert.Equal(4500, result.RiskCapital);
            Assert.Null(result.TradesToBust);
            Assert.Null(result.TradesToClutchMode);
        }

        #region Value At Risk Tests

        public record TestStopOrder(string OrderTypeId, double TriggerPrice, double Price, double Quantity);

        public static TheoryData<double, double, TestStopOrder[], double, double> ValueAtRiskScenarios => new()
        {
            // [PositionQty, SlippageTicks, Stops, ExpectedAbsVaR, ExpectedRelVaR]
            // Note: MNQ Exit Commission is $0.25 per contract.

            // 1. No Positions -> 0 VaR
            { 0, 0.0, [], 0.0, 0.0 },

            // 2. Unprotected Position -> Max Exposure (150K balance - 145.5K threshold = 4500)
            { 5, 0.0, [], 4500.0, 100.0 },

            // 3. Stop Order: Calculates distance + slippage + exit commission
            // Dist: 20 ticks * $5 * 2 qty = $200. Slippage: 2 ticks * $5 * 2 = $20. Comm: $0.25 * 2 = $0.50. Total = $220.50.
            { 2, 2.0, [new(OrderType.Stop, 4995.0, 0.0, 2)], 220.50, 220.50 / 45.0 },

            // 4. StopLimit Order: Uses Limit Price for distance
            // Dist: 40 ticks * $5 * 2 qty = $400. Comm: $0.25 * 2 = $0.50. Total = $400.50.
            { 2, 0.0, [new(OrderType.StopLimit, 4995.0, 4990.0, 2)], 400.50, 400.50 / 45.0 },

            // 5. Multiple Stop Orders: Calculates blended distance
            // Stop 1: 1 qty 20 ticks away ($100) + $0.25 comm = $100.25. 
            // Stop 2: 2 qty 40 ticks away ($400) + $0.50 comm = $400.50. 
            // Total = $500.75.
            {
                3, 0.0, [new(OrderType.Stop, 4995.0, 0.0, 1), new(OrderType.Stop, 4990.0, 0.0, 2)], 500.75,
                500.75 / 45.0
            },

            // 6. Partial Protection: Returns Max Exposure instantly
            // Pos qty = 3, Stop qty = 1. 2 contracts unprotected -> Max Exposure ($4500).
            { 3, 0.0, [new(OrderType.Stop, 4995.0, 0.0, 1)], 4500.0, 100.0 }
        };

        [Theory]
        [MemberData(nameof(ValueAtRiskScenarios))]
        public void ValueAtRisk_StandardScenarios_CalculatesCorrectly(
            double positionQty,
            double slippageTicks,
            TestStopOrder[] stops,
            double expectedAbsVaR,
            double expectedRelVaR)
        {
            // Base setup for VaR tests: Max Exposure = 150000 - 145500 = 4500
            _accountMock.SetupGet(a => a.Balance).Returns(150000);
            _symbolMock.SetupGet(s => s.TickSize).Returns(0.25);
            _symbolMock.Setup(s => s.GetTickCost(It.IsAny<double>())).Returns(5.0);
            _settingsMock.SetupGet(s => s.AverageSlippageTicks).Returns(slippageTicks);

            var positions = new List<IPosition>();
            if (positionQty > 0)
            {
                var posMock = new Mock<IPosition>();
                posMock.SetupGet(p => p.Symbol).Returns(_symbolMock.Object);
                posMock.SetupGet(p => p.Side).Returns(Side.Buy);
                posMock.SetupGet(p => p.Quantity).Returns(positionQty);
                posMock.SetupGet(p => p.OpenPrice).Returns(5000.0);
                posMock.SetupGet(p => p.Account).Returns(_accountMock.Object);
                positions.Add(posMock.Object);
            }

            var orders = new List<IOrder>();
            foreach (var stop in stops)
            {
                var orderMock = new Mock<IOrder>();
                orderMock.SetupGet(o => o.Symbol).Returns(_symbolMock.Object);
                orderMock.SetupGet(o => o.Side).Returns(Side.Sell);
                orderMock.SetupGet(o => o.OrderTypeId).Returns(stop.OrderTypeId);
                orderMock.SetupGet(o => o.TriggerPrice).Returns(stop.TriggerPrice);
                orderMock.SetupGet(o => o.Price).Returns(stop.Price);
                orderMock.SetupGet(o => o.TotalQuantity).Returns(stop.Quantity);
                orderMock.SetupGet(o => o.Status).Returns(OrderStatus.Opened);
                orders.Add(orderMock.Object);
            }

            var metrics = CreateMetrics(
                positions: positions,
                orders: orders
            );

            var result = metrics.GetAccountMetrics();

            Assert.Equal(expectedAbsVaR, result.AbsoluteValueAtRisk ?? 0.0, precision: 2);
            Assert.Equal(expectedRelVaR, result.RelativeValueAtRiskPercent ?? 0.0, precision: 2);
        }

        [Fact]
        public void ValueAtRisk_UnprotectedPosition_NoThreshold_FallsBackToBalance()
        {
            // Static account: no AutoLiquidateThresholdCurrentValue
            _accountMock.SetupGet(a => a.Balance).Returns(150000);
            _accountMock.SetupGet(a => a.Id).Returns("SimDefault");
            _accountMock
                .SetupGet(a => a.AdditionalInfo)
                .Returns(new Dictionary<string, string>());
            _settingsMock.SetupGet(s => s.MinAccountBalanceOverride).Returns(0.0);
            _settingsMock.SetupGet(s => s.DrawdownMode).Returns(DrawdownMode.Static);

            var posMock = new Mock<IPosition>();
            posMock.SetupGet(p => p.Symbol).Returns(_symbolMock.Object);
            posMock.SetupGet(p => p.Side).Returns(Side.Buy);
            posMock.SetupGet(p => p.Quantity).Returns(1);
            posMock.SetupGet(p => p.OpenPrice).Returns(5000.0);
            posMock.SetupGet(p => p.Account).Returns(_accountMock.Object);

            var metrics = CreateMetrics(positions: [posMock.Object]);

            var result = metrics.GetAccountMetrics();

            Assert.Equal(150000, result.AbsoluteValueAtRisk);
            Assert.Equal(100.0, result.RelativeValueAtRiskPercent);
        }

        #endregion
    }
}