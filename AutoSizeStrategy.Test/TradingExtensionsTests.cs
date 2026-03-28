using Moq;
using TradingPlatform.BusinessLayer;

namespace AutoSizeStrategy.Test
{
    public class TradingExtensionsTests
    {
        [Theory]
        [InlineData("TPPRO123456", DrawdownMode.Intraday)]
        [InlineData("TPPRO999", DrawdownMode.Intraday)]
        [InlineData("TPT123456", DrawdownMode.EndOfDay)]
        [InlineData("TPT888", DrawdownMode.EndOfDay)]
        [InlineData("SimPersonal", DrawdownMode.Static)]
        [InlineData("PersonalAccount", DrawdownMode.Static)]
        [InlineData("Account (USD)", DrawdownMode.Static)]
        public void InferDrawdownMode_ReturnsCorrectMode(string accountId, DrawdownMode expected)
        {
            var mock = new Mock<IAccount>();
            mock.SetupGet(a => a.Id).Returns(accountId);

            Assert.Equal(expected, mock.Object.InferDrawdownMode());
        }

        public static TheoryData<double, Side, bool> ExitDirectionScenarios => new()
        {
            // position size, side, expected return
            { 0.0, Side.Sell, false },
            { 0.0, Side.Buy, false },
            { 10.0, Side.Sell, true },
            { 10.0, Side.Buy, false },
            { -10.0, Side.Buy, true },
            { -10.0, Side.Sell, false },
        };

        [Theory]
        [MemberData(nameof(ExitDirectionScenarios))]
        public void IsExitDirection_CalculatesCorrectly(double netPosition, Side side, bool expected)
        {
            Assert.Equal(expected, side.IsExitDirection(netPosition));
        }

        public static TheoryData<double, Side, double, bool> ExitForPositionScenarios => new()
        {
            // netPos, side, orderQty, expected
            { 10.0, Side.Sell, 5.0, true }, // Partial exit
            { 10.0, Side.Sell, 10.0, true }, // Full exit
            { 10.0, Side.Sell, 15.0, false }, // Flips position (not a pure exit)
            { -10.0, Side.Buy, 5.0, true }, // Partial exit short
            { -10.0, Side.Buy, 10.0, true }, // Full exit short
            { -10.0, Side.Buy, 15.0, false }, // Flips position short
            { 10.0, Side.Buy, 5.0, false }, // Adding to long position
            { -10.0, Side.Sell, 5.0, false } // Adding to short position
        };

        [Theory]
        [MemberData(nameof(ExitForPositionScenarios))]
        public void IsExitForPosition_ChecksDirectionAndQuantity(
            double netPosition, Side side, double orderQuantity, bool expected)
        {
            // Verify IOrder extension
            var orderMock = new Mock<IOrder>();
            orderMock.SetupGet(o => o.Side).Returns(side);
            orderMock.SetupGet(o => o.TotalQuantity).Returns(orderQuantity);

            Assert.Equal(expected, orderMock.Object.IsExitForPosition(netPosition));

            // Verify IOrderRequestParameters extension
            var requestMock = new Mock<IOrderRequestParameters>();
            requestMock.SetupGet(r => r.Side).Returns(side);
            requestMock.SetupGet(r => r.Quantity).Returns(orderQuantity);

            Assert.Equal(expected, requestMock.Object.IsExitForPosition(netPosition));
        }

        public static TheoryData<string, double, double, double> OrderRequestParametersScenarios =>
            new()
            {
                // orderTypeId, price, lastPrice, expectedFillPrice
                // Market orders use Symbol.Last
                { OrderType.Market, 0.0, 5000.0, 5000.0 },
                { OrderType.Market, 4995.0, 5000.0, 5000.0 }, // Price ignored for market
                // Limit orders use the specified Price
                { OrderType.Limit, 4995.0, 5000.0, 4995.0 },
                { OrderType.Limit, 5005.0, 5000.0, 5005.0 },
                // StopLimit orders use the specified Price
                { OrderType.StopLimit, 4990.0, 5000.0, 4990.0 },
                // LimitIfTouched orders use the specified Price
                { OrderType.LimitIfTouched, 4985.0, 5000.0, 4985.0 },
            };

        [Theory]
        [MemberData(nameof(OrderRequestParametersScenarios))]
        public void GetLikelyFillPrice_OrderRequestParameters_ReturnsExpectedPrice(
            string orderTypeId,
            double price,
            double lastPrice,
            double expectedFillPrice
        )
        {
            var symbolMock = new Mock<ISymbol>();
            symbolMock.SetupGet(s => s.Last).Returns(lastPrice);

            var requestMock = new Mock<IOrderRequestParameters>();
            requestMock.SetupGet(r => r.OrderTypeId).Returns(orderTypeId);
            requestMock.SetupGet(r => r.Price).Returns(price);
            requestMock.SetupGet(r => r.Symbol).Returns(symbolMock.Object);

            double result = requestMock.Object.GetLikelyFillPrice();

            Assert.Equal(expectedFillPrice, result, precision: 6);
        }

        public static TheoryData<
            string,
            double,
            double,
            double,
            double
        > TriggerPriceOrderRequestScenarios =>
            new()
            {
                // orderTypeId, price, triggerPrice, lastPrice, expectedFillPrice
                // Stop orders use TriggerPrice (price may be NaN)
                { OrderType.Stop, double.NaN, 4980.0, 5000.0, 4980.0 },
                { OrderType.Stop, 0.0, 4980.0, 5000.0, 4980.0 },
                // MarketIfTouched orders use TriggerPrice
                { OrderType.MarketIfTouched, double.NaN, 4975.0, 5000.0, 4975.0 },
                { OrderType.MarketIfTouched, 0.0, 4975.0, 5000.0, 4975.0 },
                // TrailingStop orders use TriggerPrice
                { OrderType.TrailingStop, double.NaN, 4970.0, 5000.0, 4970.0 },
                { OrderType.TrailingStop, 0.0, 4970.0, 5000.0, 4970.0 },
            };

        [Theory]
        [MemberData(nameof(TriggerPriceOrderRequestScenarios))]
        public void GetLikelyFillPrice_TriggerPriceOrders_UsesTriggerPrice(
            string orderTypeId,
            double price,
            double triggerPrice,
            double lastPrice,
            double expectedFillPrice
        )
        {
            var symbolMock = new Mock<ISymbol>();
            symbolMock.SetupGet(s => s.Last).Returns(lastPrice);

            var requestMock = new Mock<IOrderRequestParameters>();
            requestMock.SetupGet(r => r.OrderTypeId).Returns(orderTypeId);
            requestMock.SetupGet(r => r.Price).Returns(price);
            requestMock.SetupGet(r => r.TriggerPrice).Returns(triggerPrice);
            requestMock.SetupGet(r => r.Symbol).Returns(symbolMock.Object);

            double result = requestMock.Object.GetLikelyFillPrice();

            Assert.Equal(expectedFillPrice, result, precision: 6);
        }

        [Fact]
        public void GetLikelyFillPrice_OrderRequestParameters_UnsupportedOrderType_Throws()
        {
            var symbolMock = new Mock<ISymbol>();
            symbolMock.SetupGet(s => s.Last).Returns(5000.0);

            var requestMock = new Mock<IOrderRequestParameters>();
            requestMock.SetupGet(r => r.OrderTypeId).Returns("SomeUnsupportedType");
            requestMock.SetupGet(r => r.Price).Returns(4995.0);
            requestMock.SetupGet(r => r.Symbol).Returns(symbolMock.Object);

            Assert.Throws<NotSupportedException>(() => requestMock.Object.GetLikelyFillPrice());
        }

        public static TheoryData<string, double, double, double, double> OrderScenarios =>
            new()
            {
                // orderTypeId, price, triggerPrice, lastPrice, expectedFillPrice

                // Market orders use Symbol.Last
                { OrderType.Market, 0.0, 0.0, 6000.0, 6000.0 },
                { OrderType.Market, 5995.0, 0.0, 6000.0, 6000.0 },
                // Limit orders use the order's Price
                { OrderType.Limit, 5995.0, 0.0, 6000.0, 5995.0 },
                { OrderType.Limit, 6005.0, 0.0, 6000.0, 6005.0 },
                // StopLimit orders use the order's Price (limit price after trigger)
                { OrderType.StopLimit, 5990.0, 5985.0, 6000.0, 5990.0 },
                // LimitIfTouched orders use the order's Price
                { OrderType.LimitIfTouched, 5980.0, 5975.0, 6000.0, 5980.0 },
                // Stop orders use TriggerPrice (becomes market at trigger)
                { OrderType.Stop, 0.0, 5970.0, 6000.0, 5970.0 },
                { OrderType.Stop, 5965.0, 5970.0, 6000.0, 5970.0 },
                // MarketIfTouched orders use TriggerPrice
                { OrderType.MarketIfTouched, 0.0, 5960.0, 6000.0, 5960.0 },
                // TrailingStop orders use TriggerPrice
                { OrderType.TrailingStop, 0.0, 5950.0, 6000.0, 5950.0 },
            };

        [Theory]
        [MemberData(nameof(OrderScenarios))]
        public void GetLikelyFillPrice_Order_ReturnsExpectedPrice(
            string orderTypeId,
            double price,
            double triggerPrice,
            double lastPrice,
            double expectedFillPrice
        )
        {
            var symbolMock = new Mock<ISymbol>();
            symbolMock.SetupGet(s => s.Last).Returns(lastPrice);

            var orderMock = new Mock<IOrder>();
            orderMock.SetupGet(o => o.OrderTypeId).Returns(orderTypeId);
            orderMock.SetupGet(o => o.Price).Returns(price);
            orderMock.SetupGet(o => o.TriggerPrice).Returns(triggerPrice);
            orderMock.SetupGet(o => o.Symbol).Returns(symbolMock.Object);

            double result = orderMock.Object.GetLikelyFillPrice();

            Assert.Equal(expectedFillPrice, result, precision: 6);
        }

        [Fact]
        public void GetLikelyFillPrice_Order_UnsupportedOrderType_Throws()
        {
            var symbolMock = new Mock<ISymbol>();
            symbolMock.SetupGet(s => s.Last).Returns(6000.0);

            var orderMock = new Mock<IOrder>();
            orderMock.SetupGet(o => o.OrderTypeId).Returns("SomeUnsupportedType");
            orderMock.SetupGet(o => o.Price).Returns(5995.0);
            orderMock.SetupGet(o => o.TriggerPrice).Returns(5990.0);
            orderMock.SetupGet(o => o.Symbol).Returns(symbolMock.Object);

            Assert.Throws<NotSupportedException>(() => orderMock.Object.GetLikelyFillPrice());
        }

        // A simple fake account for testing purposes
        public record TestAccount(string Id, string Name = "Test");

        [Fact]
        public void FindTargetAccount_Prioritizes_Intraday_Over_EOD()
        {
            // Arrange
            var accounts = new List<TestAccount>
            {
                new("TPT999"), // Priority 1 (EOD)
                new("TPPRO123"), // Priority 0 (Intraday) - SHOULD WIN
                new("Sim555"), // Priority 2 (Static)
            };

            // We use the generic method, telling it that 'Id' is the identifier
            var result = accounts.FindTargetAccount();

            Assert.Equal("TPPRO123", result.Id);
        }

        [Fact]
        public void FindTargetAccount_Prioritizes_EOD_Over_Static()
        {
            var accounts = new List<TestAccount>
            {
                new("SimABC"), // Priority 2
                new("TPT888"), // Priority 1 - SHOULD WIN
            };

            var result = accounts.FindTargetAccount();

            Assert.Equal("TPT888", result.Id);
        }

        [Fact]
        public void FindTargetAccount_FallsBack_To_First_If_No_Matches()
        {
            var accounts = new List<TestAccount>
            {
                new("Personal1"), // Priority 2
                new("Personal2"), // Priority 2
            };

            // MinBy is stable; it should pick the first one if ties exist
            var result = accounts.FindTargetAccount();

            Assert.Equal("Personal1", result.Id);
        }

        [Fact]
        public void FindTargetAccount_Returns_Null_For_Empty_List()
        {
            var result = new List<TestAccount>([]).FindTargetAccount();

            Assert.Null(result);
        }

        [Theory]
        // Standard Numbers
        [InlineData("123.45", 123.45, true)]
        [InlineData("0", 0.0, true)]
        [InlineData("-50.2", -50.2, true)]
        // Thousands Separators (NumberStyles.AllowThousands)
        [InlineData("1,250.50", 1250.50, true)]
        [InlineData("1,000,000", 1000000.0, true)]
        // Scientific Notation (NumberStyles.Float)
        [InlineData("1.2e3", 1200.0, true)]
        [InlineData("5E-2", 0.05, true)]
        // The "Problem" Shorthands
        [InlineData("inf", double.PositiveInfinity, true)]
        [InlineData("+inf", double.PositiveInfinity, true)]
        [InlineData("-inf", double.NegativeInfinity, true)]
        [InlineData(" INF ", double.PositiveInfinity, true)] // Case-insensitive and whitespace
        // Standard .NET Infinity/NaN
        [InlineData("Infinity", double.PositiveInfinity, true)]
        [InlineData("-Infinity", double.NegativeInfinity, true)]
        [InlineData("NaN", double.NaN, true)]
        // Invalid Cases
        [InlineData("abc", 0, false)]
        [InlineData("12.34.56", 0, false)]
        [InlineData("", 0, false)]
        [InlineData(null, 0, false)]
        public void TryParseDouble_VariousInputs_ReturnsExpected(
            string? input,
            double expectedValue,
            bool expectedSuccess
        )
        {
            bool success = input.TryParseDouble(out double result);

            Assert.Equal(expectedSuccess, success);
            if (expectedSuccess)
            {
                // Note: NaN != NaN, so we use the double checker for that specific case
                if (double.IsNaN(expectedValue))
                    Assert.True(double.IsNaN(result));
                else
                    Assert.Equal(expectedValue, result);
            }
        }

        [Theory]
        [InlineData("MNQ", true)]
        [InlineData("MES", true)]
        [InlineData("MGC", true)]
        [InlineData("MYM", true)]
        [InlineData("M2K", true)]
        [InlineData("NQ", false)]
        [InlineData("ES", false)]
        [InlineData("GC", false)]
        [InlineData("CL", false)]
        [InlineData("6E", false)]
        public void IsMicro_BySymbolName(string symbolName, bool expected)
        {
            var symbolMock = new Mock<ISymbol>();
            symbolMock.SetupGet(s => s.Name).Returns(symbolName);
            Assert.Equal(expected, symbolMock.Object.IsMicro());
        }

        [Theory]
        [InlineData("MNQ", 0.25)]
        [InlineData("MES", 0.25)]
        [InlineData("MGC", 0.25)]
        [InlineData("NQ", 2.50)]
        [InlineData("GC", 2.50)]
        [InlineData("ES", 2.50)]
        public void GetCommission_ReturnsCorrectRate(string symbolName, double expected)
        {
            var symbolMock = new Mock<ISymbol>();
            symbolMock.SetupGet(s => s.Name).Returns(symbolName);

            var settingsMock = new Mock<IStrategySettings>();
            settingsMock.SetupGet(s => s.CommissionMicro).Returns(0.25);
            settingsMock.SetupGet(s => s.CommissionMini).Returns(2.50);

            Assert.Equal(expected, settingsMock.Object.GetCommission(symbolMock.Object));
        }
    }
}
