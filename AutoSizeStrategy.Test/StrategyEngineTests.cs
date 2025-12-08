// AutoSizeStrategy.Test\StrategyEngineTests.cs
using System;
using AutoSizeStrategy;
using Moq;
using Moq.Language.Flow; // for It.Is<T>
using TradingPlatform.BusinessLayer;
using Xunit;

namespace AutoSizeStrategy.Tests
{
    /// <summary>
    /// Tests for <see cref="StrategyEngine"/>.
    /// </summary>
    public class StrategyEngineTests
    {
        private readonly Mock<IStrategyLogger> _loggerMock;
        private readonly Mock<IStrategyContext> _contextMock;
        private readonly StrategyEngine _engine;

        public StrategyEngineTests()
        {
            _loggerMock = new Mock<IStrategyLogger>();
            _contextMock = new Mock<IStrategyContext>();
            _contextMock.SetupGet(c => c.Logger).Returns(_loggerMock.Object);
            _engine = new StrategyEngine(_contextMock.Object);
        }

        #region ProcessRequest ---------------------------------------------

        [Fact]
        public void ProcessRequest_AddsTag_WhenCommentIsNull()
        {
            var request = new PlaceOrderRequestParameters { Comment = null, Quantity = 10 };

            _engine.ProcessRequest(request);

            Assert.Contains("[RiskQty:2]", request.Comment);
            Assert.Equal(2, request.Quantity); // enforced size
        }

        [Fact]
        public void ProcessRequest_AppendsTag_ToExistingComment()
        {
            var request = new PlaceOrderRequestParameters { Comment = "Start here", Quantity = 10 };

            _engine.ProcessRequest(request);

            Assert.Equal("Start here [RiskQty:2]", request.Comment);
            Assert.Equal(2, request.Quantity);
        }

        [Fact]
        public void ProcessRequest_DoesNothing_WhenNotPlaceOrder()
        {
            var nonPlaceRequest = new SomeOtherRequestParameters(); // any non‑PlaceOrder type

            _engine.ProcessRequest(nonPlaceRequest);

            // No changes expected – no exception must be thrown
        }

        [Fact]
        public void ProcessRequest_Ignores_WhenTagAlreadyPresent()
        {
            var request = new PlaceOrderRequestParameters { Comment = "[RiskQty:2]", Quantity = 5 };

            _engine.ProcessRequest(request);

            // Tag is untouched – quantity should still be original if the method exits early
            Assert.Equal("[RiskQty:2]", request.Comment);
            // The implementation sets quantity only on the first run; if early exit, quantity stays 5
            Assert.Equal(5, request.Quantity);
        }

        #endregion

        #region TryGetSizeFromTag -------------------------------------------

        [Fact]
        public void TryGetSizeFromTag_ReturnsTrue_WhenTagPresent()
        {
            var comment = "Random text [RiskQty:5] more";
            bool result = _engine.TryGetSizeFromTag(comment, string.Empty, out int size);

            Assert.True(result);
            Assert.Equal(5, size);
        }

        [Fact]
        public void TryGetSizeFromTag_ReturnsFalse_WhenTagMissing()
        {
            var comment = "No tag here";
            bool result = _engine.TryGetSizeFromTag(comment, string.Empty, out int size);

            Assert.False(result);
            Assert.Equal(0, size);
        }

        #endregion

        #region ProcessFailSafe -----------------------------------------------------------

        [Fact]
        public void ProcessFailSafe_LogsWarning_WhenNoTag()
        {
            var order = new Mock<IOrder>();
            order.SetupGet(o => o.Status).Returns(OrderStatus.Opened);
            order.SetupGet(o => o.TotalQuantity).Returns(2);
            order.SetupGet(o => o.Comment).Returns("Untagged comment");
            order.SetupGet(o => o.Id).Returns("123");

            _engine.ProcessFailSafe(order.Object);

            _loggerMock.Verify(
                l => l.LogInfo(It.Is<string>(s => s.Contains("does not have a size tag"))),
                Times.Once
            );
            order.Verify(o => o.Cancel(), Times.Never);
        }

        [Fact]
        public void ProcessFailSafe_Cancels_WhenQuantityMismatch()
        {
            var order = new Mock<IOrder>();
            order.SetupGet(o => o.Status).Returns(OrderStatus.Opened);
            order.SetupGet(o => o.TotalQuantity).Returns(5); // wrong size
            order.SetupGet(o => o.Comment).Returns("[RiskQty:2]");
            order.SetupGet(o => o.Id).Returns("456");

            _engine.ProcessFailSafe(order.Object);

            _loggerMock.Verify(
                l => l.LogInfo(It.Is<string>(s => s.Contains("Killing Order"))),
                Times.Once
            );
            order.Verify(o => o.Cancel(), Times.Once);
        }

        [Fact]
        public void ProcessFailSafe_DoesNothing_WhenOrderClosed()
        {
            var order = new Mock<IOrder>();
            // Closed order – should skip
            order.SetupGet(o => o.Status).Returns(OrderStatus.Cancelled);

            _engine.ProcessFailSafe(order.Object);

            _loggerMock.VerifyNoOtherCalls();
            order.VerifyGet(o => o.Status, Times.AtLeastOnce);
        }

        [Fact]
        public void ProcessRequest_LogsEnforceInfo_WhenQuantityAdjusted()
        {
            // Quantity is 5 -> should be changed to 2
            var request = new PlaceOrderRequestParameters { Comment = null, Quantity = 5 };

            _engine.ProcessRequest(request);

            Assert.Equal(2, request.Quantity);
            _loggerMock.Verify(
                l => l.LogInfo(It.Is<string>(msg => msg.Contains("[Risk Enforced]"))),
                Times.Once
            );
        }

        [Fact]
        public void ProcessRequest_DoesNotLog_WhenTagAlreadyPresent()
        {
            var request = new PlaceOrderRequestParameters { Comment = "[RiskQty:2]", Quantity = 5 };

            _engine.ProcessRequest(request);

            // No LogInfo should be executed because the method exits early
            _loggerMock.Verify(l => l.LogInfo(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void TryGetSizeFromTag_ReturnsFalse_WhenNullComment()
        {
            bool result = _engine.TryGetSizeFromTag(null, "XYZ", out int size);

            Assert.False(result);
            Assert.Equal(0, size);
            _loggerMock.VerifyNoOtherCalls(); // no error logged
        }

        [Fact]
        public void TryGetSizeFromTag_LogsError_WhenInvalidTag()
        {
            var comment = "[RiskQty:ABC]";

            bool result = _engine.TryGetSizeFromTag(comment, "XYZ", out int size);

            Assert.False(result);
            Assert.Equal(0, size);
            _loggerMock.Verify(
                l => l.LogError(It.Is<string>(s => s.Contains("invalid validation tag"))),
                Times.Once
            );
        }

        [Fact]
        public void ProcessFailSafe_LogsError_WhenCancelThrows()
        {
            var order = new Mock<IOrder>();
            order.SetupGet(o => o.Status).Returns(OrderStatus.Opened);
            order.SetupGet(o => o.TotalQuantity).Returns(5); // mismatch
            order.SetupGet(o => o.Comment).Returns("[RiskQty:2]");
            order.SetupGet(o => o.Id).Returns("XYZ");
            order.Setup(o => o.Cancel()).Throws<InvalidOperationException>(); // simulate failure

            _engine.ProcessFailSafe(order.Object);

            _loggerMock.Verify(
                l => l.LogError(It.Is<string>(s => s.Contains("cancelation failed"))),
                Times.Once
            );
        }

        #endregion
    }

    // ------------------------------------------------------------

    /// <summary>
    /// A dummy class used only to exercise the non‑`PlaceOrderRequestParameters`
    /// branch of <see cref="StrategyEngine.ProcessRequest"/>.
    /// The real project will have other request types – we just need one here.
    /// </summary>
    public class SomeOtherRequestParameters : RequestParameters
    {
        // Stub implementation of the abstract Type property.
        // The exact enum value is irrelevant for the unit tests,
        // so we cast 0 to the enum type.
        public override RequestType Type => (RequestType)0;
    }
}
