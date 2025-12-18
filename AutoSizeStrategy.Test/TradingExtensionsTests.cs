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
                // Format: { netPosition, side, expectedResult }
                { 0.0, Side.Sell, false }, // Flat -> Sell is entry (Short)
                { 0.0, Side.Buy, false }, // Flat -> Buy is entry (Long)
                { 10.0, Side.Sell, true }, // Long -> Sell reduces
                { 10.0, Side.Buy, false }, // Long -> Buy adds risk
                { -10.0, Side.Buy, true }, // Short -> Buy reduces
                { -10.0, Side.Sell, false }, // Short -> Sell adds risk
            };

        [Theory]
        [MemberData(nameof(ReduceOnlyScenarios))]
        public void IsReduceOnly_CalculatesCorrectly_ForAllTypes(
            double netPosition,
            Side side,
            bool expected
        )
        {
            // Verify Core Logic (Side extension)
            Assert.Equal(expected, side.IsReduceOnly(netPosition));

            // Verify IOrder extension delegates correctly
            var orderMock = new Mock<IOrder>();
            orderMock.SetupGet(o => o.Side).Returns(side);
            Assert.Equal(expected, orderMock.Object.IsReduceOnly(netPosition));

            // Verify IOrderRequestParameters extension delegates correctly
            var requestMock = new Mock<IOrderRequestParameters>();
            requestMock.SetupGet(r => r.Side).Returns(side);
            Assert.Equal(expected, requestMock.Object.IsReduceOnly(netPosition));
        }
    }
}
