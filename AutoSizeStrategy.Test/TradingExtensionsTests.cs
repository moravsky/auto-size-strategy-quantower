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
    }
}
