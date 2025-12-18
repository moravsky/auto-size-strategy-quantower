using AutoSizeStrategy;
using Moq;
using TradingPlatform.BusinessLayer;
using Xunit;

namespace AutoSizeStrategy.Tests
{
    public class OrderExtensionsTests
    {
        [Fact]
        public void IsReduceOnly_FlatPosition_ReturnsFalse()
        {
            var order = CreateOrder(Side.Sell);
            // Flat position -> Sell is an entry (Short), not a reduce.
            Assert.False(order.Object.IsReduceOnly(netPosition: 0));
        }

        [Fact]
        public void IsReduceOnly_LongPosition_SellReturnsTrue()
        {
            var order = CreateOrder(Side.Sell);
            // Long 10 -> Sell reduces.
            Assert.True(order.Object.IsReduceOnly(netPosition: 10));
        }

        [Fact]
        public void IsReduceOnly_LongPosition_BuyReturnsFalse()
        {
            var order = CreateOrder(Side.Buy);
            // Long 10 -> Buy adds to risk.
            Assert.False(order.Object.IsReduceOnly(netPosition: 10));
        }

        [Fact]
        public void IsReduceOnly_ShortPosition_BuyReturnsTrue()
        {
            var order = CreateOrder(Side.Buy);
            // Short -10 -> Buy reduces.
            Assert.True(order.Object.IsReduceOnly(netPosition: -10));
        }

        private static Mock<IOrder> CreateOrder(Side side)
        {
            var mock = new Mock<IOrder>();
            mock.SetupGet(o => o.Side).Returns(side);
            return mock;
        }
    }
}
