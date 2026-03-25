using Moq;
using TradingPlatform.BusinessLayer;

namespace AutoSizeStrategy.Test
{
    public class TradingServiceTests : IDisposable
    {
        private readonly Mock<IStrategyLogger> _loggerMock;
        private readonly TradingService _service;

        public TradingServiceTests()
        {
            _loggerMock = new Mock<IStrategyLogger>();
            _service = new TradingService(_loggerMock.Object);
        }

        [Fact]
        public async Task Place_Timeout_Logs()
        {
            const long expectedRequestId = 123456789L;
            var requestMock = new Mock<IPlaceOrderRequestParameters>();

            // Setup a counter and a signal
            int sendCallCount = 0;
            var thirdCallReached = new TaskCompletionSource<bool>();

            requestMock.SetupGet(r => r.RequestId).Returns(expectedRequestId);
            // Simulate persistent failure to trigger the final timeout log
            _ = requestMock
                .Setup(r => r.Send())
                .Callback(() =>
                {
                    sendCallCount++;
                    if (sendCallCount == 4)
                        thirdCallReached.TrySetResult(true);
                })
                .Returns(TradingOperationResult.CreateError(expectedRequestId, "No liquidity"));

            _service.Place(requestMock.Object);

            // We must wait for retries to exhaust (Initial 400ms -> 800ms -> 1600ms + jitters)
            var completedTask = await Task.WhenAny(thirdCallReached.Task, Task.Delay(10000));
            Assert.True(completedTask == thirdCallReached.Task, "Timed out waiting for 3rd retry");
            // Verify the ID was actually retrieved from the request object
            requestMock.VerifyGet(r => r.RequestId, Times.AtLeastOnce);

            // Verify Send was called exactly 4 times (1 + MaxRetries)
            requestMock.Verify(r => r.Send(), Times.Exactly(4));

            // Verify the log contains the EXACT RequestId and operation name
            _loggerMock.Verify(
                l =>
                    l.LogError(
                        It.Is<string>(s =>
                            s.Contains("PlaceRequest")
                            && s.Contains(expectedRequestId.ToString())
                            && s.Contains("timed out")
                        )
                    ),
                Times.Once
            );
        }

        [Fact]
        public async Task Cancel_ChecksStatus_AndDoesNotExecuteIfClosed()
        {
            var orderMock = new Mock<IOrder>();
            orderMock.SetupGet(o => o.Id).Returns("ALREADY-CLOSED");
            orderMock.SetupGet(o => o.Status).Returns(OrderStatus.Cancelled); // Already closed

            _service.Cancel(orderMock.Object);
            await Task.Delay(1000);

            orderMock.VerifyGet(o => o.Status, Times.AtLeastOnce);
            orderMock.Verify(o => o.Cancel(), Times.Never);
        }

        [Fact]
        public async Task CancelReplace_FullWorkflow_CompletesSuccessfully()
        {
            const string originalOrderId = "order1";
            const long newRequestId = 101010L;

            // 1. Create signals for the specific background events
            var cancelCalledTcs = new TaskCompletionSource<bool>();
            var sendCalledTcs = new TaskCompletionSource<bool>();

            var orderMock = new Mock<IOrder>();
            orderMock.SetupGet(o => o.Id).Returns(originalOrderId);
            orderMock.SetupGet(o => o.Status).Returns(OrderStatus.Opened);

            // Signal the test thread the moment Cancel() is actually invoked
            orderMock
                .Setup(o => o.Cancel())
                .Callback(() => cancelCalledTcs.TrySetResult(true))
                .Returns(TradingOperationResult.CreateSuccess(123, originalOrderId));

            var newParamsMock = new Mock<IPlaceOrderRequestParameters>();
            newParamsMock.SetupGet(p => p.Quantity).Returns(67);
            newParamsMock.SetupGet(p => p.RequestId).Returns(newRequestId);

            // Signal the test thread the moment Send() is invoked
            newParamsMock
                .Setup(p => p.Send())
                .Callback(() => sendCalledTcs.TrySetResult(true))
                .Returns(TradingOperationResult.CreateSuccess(newRequestId, originalOrderId));

            _service.CancelReplace(orderMock.Object, newParamsMock.Object);

            // Wait for the initial Cancel() to be triggered by the background task
            // This ensures the order is actually being tracked before we report it cancelled.
            await cancelCalledTcs.Task;

            // Simulate the platform event
            _service.ReportCancelledOrder(originalOrderId);

            // Wait for the subsequent Send() to be invoked (with a safety timeout)
            var completedTask = await Task.WhenAny(sendCalledTcs.Task, Task.Delay(5000));

            // Assert
            Assert.True(completedTask == sendCalledTcs.Task, "Timed out waiting for p.Send()");
            orderMock.Verify(o => o.Cancel(), Times.Once);
            newParamsMock.Verify(p => p.Send(), Times.Once);
        }

        [Fact]
        public async Task CancelReplace_TimeoutOnSpecificId_Logs()
        {
            const string targetId = "STUCK-ID-67";
            var orderMock = new Mock<IOrder>();
            orderMock.SetupGet(o => o.Id).Returns(targetId);
            orderMock.SetupGet(o => o.Status).Returns(OrderStatus.Opened);

            var newParams = new Mock<IPlaceOrderRequestParameters>();
            _service.CancelReplace(orderMock.Object, newParams.Object);

            // We wait longer than the default 5s TrackingSet timeout
            await Task.Delay(6000);

            // Verify that the error message explicitly names the stuck order
            _loggerMock.Verify(
                l =>
                    l.LogError(
                        It.Is<string>(s =>
                            s.Contains("CancelReplace")
                            && s.Contains("Timeout")
                            && s.Contains(targetId)
                        )
                    ),
                Times.Once
            );

            // Verify the new order was never placed
            newParams.Verify(p => p.Send(), Times.Never);
        }

        [Fact]
        public async Task CancelReplace_ZeroQuantity_CancelsButDoesNotPlace()
        {
            var cancelCalledTcs = new TaskCompletionSource<bool>();

            var orderMock = new Mock<IOrder>();
            orderMock.SetupGet(o => o.Id).Returns("order-zero-qty");
            orderMock.SetupGet(o => o.Status).Returns(OrderStatus.Opened);
            orderMock
                .Setup(o => o.Cancel())
                .Callback(() => cancelCalledTcs.TrySetResult(true))
                .Returns(TradingOperationResult.CreateSuccess(123, "order-zero-qty"));

            var newParamsMock = new Mock<IPlaceOrderRequestParameters>();
            newParamsMock.SetupGet(p => p.Quantity).Returns(0.0);
            newParamsMock.SetupGet(p => p.RequestId).Returns(555L);

            _service.CancelReplace(orderMock.Object, newParamsMock.Object);

            await cancelCalledTcs.Task;
            _service.ReportCancelledOrder("order-zero-qty");

            await Task.Delay(500);

            orderMock.Verify(o => o.Cancel(), Times.Once);
            newParamsMock.Verify(p => p.Send(), Times.Never);
        }

        [Fact]
        public void Dispose_CleanlyStopsAllActivity()
        {
            var loggerMock = new Mock<IStrategyLogger>();
            var localService = new TradingService(loggerMock.Object);

            localService.Dispose();

            // No background tasks should trigger logs after disposal
            loggerMock.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public async Task Cancel_Aborts_WhenStatusChangesToCancelled(int openedReturns)
        {
            var tcs = new TaskCompletionSource();
            var orderMock = new Mock<IOrder>();
            orderMock.SetupGet(o => o.Id).Returns("RACE-CONDITION-ID");

            var statusSetup = orderMock.SetupSequence(o => o.Status);
            for (int i = 0; i < openedReturns; i++)
                statusSetup.Returns(OrderStatus.Opened);
            statusSetup.Returns(OrderStatus.Cancelled);

            _loggerMock
                .Setup(l => l.LogInfo(It.Is<string>(s => s.Contains("aborted"))))
                .Callback(tcs.SetResult);

            _service.Cancel(orderMock.Object, useLeadingJitter: true);

            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

            _loggerMock.Verify(
                l => l.LogInfo(It.Is<string>(s => s.Contains("aborted"))),
                Times.Once
            );
            orderMock.Verify(o => o.Cancel(), Times.Never);
        }

        [Fact]
        public async Task Cancel_Aborts_WhenReportedCancelledImmediately()
        {
            var orderMock = new Mock<IOrder>();
            orderMock.SetupGet(o => o.Id).Returns("IDEMPOTENT-ID");
            orderMock.SetupGet(o => o.Status).Returns(OrderStatus.Opened);

            var tcs = new TaskCompletionSource();
            _loggerMock
                .Setup(l => l.LogInfo(It.Is<string>(s => s.Contains("aborted"))))
                .Callback(tcs.SetResult);

            // Simulate the platform event
            _service.Cancel(orderMock.Object, useLeadingJitter: true);
            _service.ReportCancelledOrder("IDEMPOTENT-ID");

            // We wait for the "aborted" log message
            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // Never called, because we reported order as cancelled before the background task had a chance to execute.
            orderMock.Verify(o => o.Cancel(), Times.Never);

            _loggerMock.Verify(
                l => l.LogInfo(It.Is<string>(s => s.Contains("aborted"))),
                Times.Once
            );
        }

        [Fact]
        public async Task Place_Aborts_WhenCancellationTokenSignaled()
        {
            var cancellationToken = new CancellationToken(canceled: true);
            var requestMock = new Mock<IPlaceOrderRequestParameters>();
            requestMock.SetupGet(r => r.RequestId).Returns(999L);
            requestMock.SetupGet(r => r.CancellationToken).Returns(cancellationToken);

            // Start Place
            _service.Place(requestMock.Object, useLeadingJitter: true);

            await Task.Delay(100, CancellationToken.None); // Wait for the background task to start

            requestMock.Verify(r => r.Send(), Times.Never);
            _loggerMock.Verify(
                l => l.LogInfo(It.Is<string>(s => s.Contains("aborted"))),
                Times.Once
            );
        }

        public void Dispose() => _service.Dispose();
    }
}
