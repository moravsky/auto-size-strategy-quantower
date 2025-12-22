using System;
using System.Threading.Tasks;
using AutoSizeStrategy;
using Moq;
using TradingPlatform.BusinessLayer;
using Xunit;

namespace AutoSizeStrategy.Tests
{
    public class OrderKillerTests
    {
        private readonly Mock<IStrategyLogger> _loggerMock;
        private readonly OrderKiller _orderKiller;

        // Constants from OrderKiller.cs for test timing
        // MinCancelDelay = 567, MaxCancelDelay = 1234
        // We use a safe buffer (e.g. 2000ms) to ensure the background task has completed.
        private const int SafeWaitTimeMs = 2000;

        public OrderKillerTests()
        {
            _loggerMock = new Mock<IStrategyLogger>();
            _orderKiller = new OrderKiller(_loggerMock.Object);
        }

        // TODO: V3: Inject RNG with a seed
        [Fact]
        public async Task Kill_OpenedOrder_CallsCancelAfterDelay()
        {
            var orderMock = CreateMockOrder("id_1", OrderStatus.Opened);

            _orderKiller.Kill(orderMock.Object);

            // Immediate check: Cancel should NOT be called yet due to Jitter delay
            orderMock.Verify(
                o => o.Cancel(),
                Times.Never,
                "Cancel was called too early (jitter ignored)"
            );

            // Wait for the background task/jitter to finish
            await Task.Delay(SafeWaitTimeMs);

            // Final check
            orderMock.Verify(
                o => o.Cancel(),
                Times.Once,
                "Cancel was not called after the jitter period"
            );
        }

        [Theory]
        // Not yet open at exchange
        [InlineData(OrderStatus.Cancelled)]
        [InlineData(OrderStatus.Filled)]
        [InlineData(OrderStatus.Inactive)]
        [InlineData(OrderStatus.PartiallyFilled)]
        [InlineData(OrderStatus.Refused)]
        [InlineData(OrderStatus.Unspecified)]
        public void Kill_IgnoresOrders_UnlessOpened(OrderStatus status)
        {
            var orderMock = CreateMockOrder("id_ignore", status);

            _orderKiller.Kill(orderMock.Object);

            // We don't even need to wait for the delay here;
            // the logic should exit immediately before spawning the Task.
            orderMock.Verify(o => o.Cancel(), Times.Never);
        }

        [Fact]
        public async Task Kill_CalledTwiceRapidly_CancelsOnlyOnce()
        {
            var orderMock = CreateMockOrder("id_3", OrderStatus.Opened);

            _orderKiller.Kill(orderMock.Object);
            _orderKiller.Kill(orderMock.Object); // Second call should be ignored due to lock

            await Task.Delay(SafeWaitTimeMs);

            orderMock.Verify(o => o.Cancel(), Times.Once);
        }

        [Fact]
        public async Task ReportCancelledOrder_RemovesLock_AllowingSecondKill()
        {
            var orderMock = CreateMockOrder("id_4", OrderStatus.Opened);

            // First Kill
            _orderKiller.Kill(orderMock.Object);
            await Task.Delay(SafeWaitTimeMs);
            orderMock.Verify(o => o.Cancel(), Times.Once);

            // Report it as cancelled (clears lock)
            _orderKiller.ReportCancelledOrder("id_4");

            // Kill again (simulating a case where it didn't actually close or reopened)
            _orderKiller.Kill(orderMock.Object);
            await Task.Delay(SafeWaitTimeMs);

            orderMock.Verify(o => o.Cancel(), Times.Exactly(2));
        }

        [Fact]
        public async Task Kill_LogsError_WhenCancelFails()
        {
            // Arrange
            var orderMock = new Mock<IOrder>();
            orderMock.SetupGet(o => o.Id).Returns("id_fail");
            orderMock.SetupGet(o => o.Status).Returns(OrderStatus.Opened);

            // Setup Cancel to return failure
            var failureResult = TradingOperationResult.CreateError(5411, "API Error");
            orderMock.Setup(o => o.Cancel()).Returns(failureResult);

            _orderKiller.Kill(orderMock.Object);
            await Task.Delay(SafeWaitTimeMs);

            _loggerMock.Verify(
                l => l.LogError(It.Is<string>(s => s.Contains("cancelation failed"))),
                Times.Once
            );
        }

        [Fact]
        public async Task Kill_LogsError_WhenCancelThrowsException()
        {
            // Arrange
            var orderMock = new Mock<IOrder>();
            orderMock.SetupGet(o => o.Id).Returns("id_ex");
            orderMock.SetupGet(o => o.Status).Returns(OrderStatus.Opened);

            // Setup Cancel to throw
            orderMock.Setup(o => o.Cancel()).Throws(new Exception("Network error"));

            // Act
            _orderKiller.Kill(orderMock.Object);
            await Task.Delay(SafeWaitTimeMs);

            // Assert
            _loggerMock.Verify(
                l => l.LogError(It.Is<string>(s => s.Contains("Network error"))),
                Times.Once
            );
        }

        private static Mock<IOrder> CreateMockOrder(string id, OrderStatus status)
        {
            var mock = new Mock<IOrder>();
            mock.SetupGet(o => o.Id).Returns(id);
            mock.SetupGet(o => o.Status).Returns(status);

            // Default successful cancel
            mock.Setup(o => o.Cancel()).Returns(TradingOperationResult.CreateSuccess(1234, id));

            return mock;
        }
    }
}
