using System;
using System.Threading.Tasks;
using AutoSizeStrategy;
using Moq;
using TradingPlatform.BusinessLayer;
using Xunit;

namespace AutoSizeStrategy.Tests
{
    public class TradingExtensionsTests
    {
        public static TheoryData<double, Side, bool> ReduceOnlyScenarios =>
            new()
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
        [MemberData(nameof(ReduceOnlyScenarios))]
        public void IsReduceOnlyForPosition_CalculatesCorrectly(
            double netPosition,
            Side side,
            bool expected
        )
        {
            // Verify Side extension
            Assert.Equal(expected, side.IsReduceOnlyForPosition(netPosition));

            // Verify IOrder extension
            var orderMock = new Mock<IOrder>();
            orderMock.SetupGet(o => o.Side).Returns(side);
            Assert.Equal(expected, orderMock.Object.IsReduceOnlyForPosition(netPosition));

            // Verify IOrderRequestParameters extension
            var requestMock = new Mock<IOrderRequestParameters>();
            requestMock.SetupGet(r => r.Side).Returns(side);
            Assert.Equal(expected, requestMock.Object.IsReduceOnlyForPosition(netPosition));
        }

        [Fact]
        public async Task IsReduceOnlyAsync_FastPath_ReturnsImmediately()
        {
            var contextMock = new Mock<IStrategyContext>();
            var orderMock = new Mock<IOrder>();

            contextMock
                .Setup(c => c.GetNetPositionQuantity(It.IsAny<IAccount>(), It.IsAny<ISymbol>()))
                .Returns(10.0);

            orderMock.SetupGet(o => o.Side).Returns(Side.Sell);

            bool result = await orderMock.Object.IsReduceOnlyAsync(contextMock.Object);
            Assert.True(result);
        }

        [Fact]
        public async Task IsReduceOnlyAsync_DelayedPostionInfo_RetriesAndSucceeds()
        {
            var contextMock = new Mock<IStrategyContext>();
            var orderMock = new Mock<IOrder>();
            orderMock.SetupGet(o => o.Side).Returns(Side.Sell);

            contextMock
                .SetupSequence(c =>
                    c.GetNetPositionQuantity(It.IsAny<IAccount>(), It.IsAny<ISymbol>())
                )
                .Returns(0)
                .Returns(0)
                .Returns(10);

            bool result = await orderMock.Object.IsReduceOnlyAsync(
                contextMock.Object,
                maxWait: TimeSpan.FromMilliseconds(100),
                retryInterval: TimeSpan.FromMilliseconds(10)
            );

            Assert.True(result);
        }

        [Fact]
        public async Task IsReduceOnlyAsync_EntryOrder_ReturnsFalse()
        {
            var contextMock = new Mock<IStrategyContext>();
            var orderMock = new Mock<IOrder>();
            orderMock.SetupGet(o => o.Side).Returns(Side.Sell);

            contextMock
                .SetupSequence(c =>
                    c.GetNetPositionQuantity(It.IsAny<IAccount>(), It.IsAny<ISymbol>())
                )
                .Returns(0); // no position

            bool result = await orderMock.Object.IsReduceOnlyAsync(
                contextMock.Object,
                maxWait: TimeSpan.FromMilliseconds(50),
                retryInterval: TimeSpan.FromMilliseconds(20)
            );

            Assert.False(result);
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
                // Stop orders use the specified Price (for requests)
                { OrderType.Stop, 4980.0, 5000.0, 4980.0 },
                // MarketIfTouched orders use the specified Price (for requests)
                { OrderType.MarketIfTouched, 4975.0, 5000.0, 4975.0 },
                // TrailingStop orders use the specified Price (for requests)
                { OrderType.TrailingStop, 4970.0, 5000.0, 4970.0 },
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

            // Act
            // We use the generic method, telling it that 'Id' is the identifier
            var result = accounts.FindTargetAccount();

            // Assert
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
            var accounts = new List<TestAccount>();

            var result = accounts.FindTargetAccount();

            Assert.Null(result);
        }
    }
}
