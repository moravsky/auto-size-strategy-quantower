using System;
using System.Collections.Generic;
using System.Threading;
using AutoSizeStrategy;
using Moq;
using Moq.Language.Flow;
using TradingPlatform.BusinessLayer;
using Xunit;

namespace AutoSizeStrategy.Tests
{
    public class StrategyEngineTests
    {
        private readonly Mock<IStrategyLogger> _loggerMock;
        private readonly Mock<IStrategyContext> _contextMock;
        private readonly Mock<IStrategySettings> _settingsMock;
        private readonly Mock<IAccount> _accountMock;
        private readonly Mock<ISymbol> _symbolMock;
        private readonly StrategyEngine _engine;

        public StrategyEngineTests()
        {
            _loggerMock = new Mock<IStrategyLogger>();
            _contextMock = new Mock<IStrategyContext>();
            _settingsMock = new Mock<IStrategySettings>();
            _accountMock = new Mock<IAccount>();
            _symbolMock = new Mock<ISymbol>();

            _contextMock.SetupGet(c => c.Logger).Returns(_loggerMock.Object);
            _contextMock.SetupGet(c => c.Settings).Returns(_settingsMock.Object);

            _settingsMock.SetupGet(s => s.RiskPercent).Returns(10.0);
            _settingsMock
                .SetupGet(s => s.MissingStopLossAction)
                .Returns(MissingStopLossAction.Reject);

            // Default Account (Sim)
            _accountMock.SetupGet(a => a.Id).Returns("SimDefault");
            _accountMock.SetupGet(a => a.Balance).Returns(150_000.0);

            // Default Symbol (MES-like)
            _symbolMock.SetupGet(s => s.TickSize).Returns(0.25);
            _symbolMock.SetupGet(s => s.Last).Returns(5000.0);
            _symbolMock.Setup(s => s.GetTickCost(It.IsAny<double>())).Returns(5.0); // $5/tick

            _engine = new StrategyEngine(_contextMock.Object);
        }

        #region Drawdown Logic Tests (Intraday vs EOD vs Static) ----------------

        [Fact]
        public void ProcessRequest_IntradayAccount_SubtractsBufferFromBalance()
        {
            _accountMock.SetupGet(a => a.Id).Returns("TPPRO123456");

            // Balance: 150,000
            // Risk Budget = 150,000 - 145,500 (Hardcoded Buffer) = $4,500
            // Risk (10%) = $450
            _accountMock.SetupGet(a => a.Balance).Returns(150_000.0);

            // Create Request with specific risk per contract
            // Stop: 5 pts = 20 ticks. Value: 20 ticks * $5 = $100 risk per contract.
            // Expected Qty: $450 / $100 = 4.5 -> Round down to 4.
            var request = CreateValidRequest(quantity: 100, stopDistancePoints: 5);

            _engine.ProcessRequest(request);

            Assert.Contains("[RiskQty:4]", request.Comment);
            Assert.Equal(4, request.Quantity);
        }

        [Fact]
        public void ProcessRequest_EODAccount_SubtractsBufferFromBalance()
        {
            _accountMock.SetupGet(a => a.Id).Returns("TPT987654");

            // Balance: 150,000
            // Risk budget = $4,500 -> Risk = $450
            _accountMock.SetupGet(a => a.Balance).Returns(150_000.0);

            // Same risk parameters ($100 risk per contract)
            var request = CreateValidRequest(quantity: 100, stopDistancePoints: 5);

            _engine.ProcessRequest(request);

            Assert.Contains("[RiskQty:4]", request.Comment);
            Assert.Equal(4, request.Quantity);
        }

        [Fact]
        public void ProcessRequest_StaticAccount_UsesFullBalance()
        {
            _accountMock.SetupGet(a => a.Id).Returns("SimPersonalAccount");

            // Balance: 150,000
            // Risk budget = $150,000 (No buffer subtraction)
            // Risk (10%) = $15,000
            _accountMock.SetupGet(a => a.Balance).Returns(150_000.0);

            // 3. Same risk parameters ($100 risk per contract)
            // Expected Qty: $15,000 / $100 = 150.
            var request = CreateValidRequest(quantity: 1000, stopDistancePoints: 5);

            _engine.ProcessRequest(request);

            Assert.Contains("[RiskQty:150]", request.Comment);
            Assert.Equal(150, request.Quantity);
        }

        #endregion

        #region ProcessRequest ---------------------------------------------

        [Fact]
        public void ProcessRequest_Sdk_PlaceOrderRequestParameters_WrappedCorrectly()
        {
            var request = new PlaceOrderRequestParameters { Comment = "[RiskQty:2]", Quantity = 2 };
            _engine.ProcessRequest(request);

            Assert.Contains("[RiskQty:2]", request.Comment); // passed through comment
            Assert.Equal(2, request.Quantity); // passed through size
            _loggerMock.Verify(
                l =>
                    l.LogInfo(
                        It.Is<string>(s =>
                            s.Contains("has [RiskQty: comment - passing through unchanged")
                        )
                    ),
                Times.Once
            );
        }

        [Fact]
        public void ProcessRequest_AddsTag_WhenCommentIsNull()
        {
            _accountMock.SetupGet(a => a.Balance).Returns(2000.0);
            // Risk $200. Stop 5pts ($100/contract). Size = 2.
            var request = CreateValidRequest(quantity: 10, stopDistancePoints: 5);

            _engine.ProcessRequest(request);

            Assert.Contains("[RiskQty:2]", request.Comment);
            Assert.Equal(2, request.Quantity); // enforced size
        }

        [Fact]
        public void ProcessRequest_Cancels_WhenNoStopLossReject()
        {
            _settingsMock
                .SetupGet(s => s.MissingStopLossAction)
                .Returns(MissingStopLossAction.Reject);

            var request = CreateValidRequest(quantity: 10, stopDistancePoints: 5);
            request.StopLossItems.Clear();

            _engine.ProcessRequest(request);

            Assert.Equal(0, request.Quantity);
            _loggerMock.Verify(
                l => l.LogInfo(It.Is<string>(s => s.Contains("cancelled: stop loss required"))),
                Times.Once
            );
        }

        [Fact]
        public void ProcessRequest_Ignores_WhenNoStopLossIgnore()
        {
            _settingsMock
                .SetupGet(s => s.MissingStopLossAction)
                .Returns(MissingStopLossAction.Ignore);

            var request = CreateValidRequest(quantity: 10, stopDistancePoints: 5);
            request.StopLossItems.Clear();

            _engine.ProcessRequest(request);

            Assert.Equal(10, request.Quantity);
            _loggerMock.Verify(
                l => l.LogInfo(It.Is<string>(s => s.Contains("passing through unchanged"))),
                Times.Once
            );
        }

        [Fact]
        public void ProcessRequest_AppendsTag_ToExistingComment()
        {
            var request = CreateValidRequest(
                comment: "Start here",
                quantity: 10,
                stopDistancePoints: 5
            );

            _engine.ProcessRequest(request);

            Assert.Equal("Start here [RiskQty:150]", request.Comment);
            Assert.Equal(150, request.Quantity);
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
            var request = CreateValidRequest(
                comment: "[RiskQty:2]",
                quantity: 5,
                stopDistancePoints: 10
            );

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

        #endregion

        #region ProcessFailSafe -------------------------------------------

        [Fact]
        public void ProcessFailSafe_LogsWarning_WhenNoTag()
        {
            var order = CreateMockOrder(id: "123", qty: 2, comment: "Untagged");

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
            var order = CreateMockOrder(id: "456", qty: 5, comment: "[RiskQty:2]");

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
            // Quantity is 1 -> should be changed to 75 by the strategy
            var request = CreateValidRequest(quantity: 1, stopDistancePoints: 10);

            _engine.ProcessRequest(request);

            Assert.Equal(75, request.Quantity);
            _loggerMock.Verify(
                l => l.LogInfo(It.Is<string>(msg => msg.Contains("[Risk Enforced]"))),
                Times.Once
            );
        }

        [Fact]
        public void ProcessRequest_Logs_WhenTagAlreadyPresent()
        {
            var request = CreateValidRequest(
                comment: "[RiskQty:2]",
                quantity: 5,
                stopDistancePoints: 10
            );

            _engine.ProcessRequest(request);

            _loggerMock.Verify(
                l =>
                    l.LogInfo(
                        It.Is<string>(s =>
                            s.Contains("has [RiskQty: comment - passing through unchanged")
                        )
                    ),
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

        #region Helpers ---------------------------------------------------

        private PlaceOrderRequestParametersWrapper CreateValidRequest(
            double quantity,
            double stopDistancePoints,
            string? comment = null
        )
        {
            double currentPrice = _symbolMock.Object.Last;
            double stopPrice = currentPrice - stopDistancePoints;

            var slTpHolder = SlTpHolder.CreateSL(stopPrice, PriceMeasurement.Absolute);

            return new PlaceOrderRequestParametersWrapper
            {
                Comment = comment,
                Quantity = quantity,
                Account = _accountMock.Object,
                Symbol = _symbolMock.Object,
                Price = currentPrice,
                StopLossItems = [slTpHolder],
            };
        }

        private static Mock<IOrder> CreateMockOrder(string id, double qty, string comment)
        {
            var order = new Mock<IOrder>();
            order.SetupGet(o => o.Status).Returns(OrderStatus.Opened);
            order.SetupGet(o => o.TotalQuantity).Returns(qty);
            order.SetupGet(o => o.Comment).Returns(comment);
            order.SetupGet(o => o.Id).Returns(id);
            return order;
        }

        #endregion
    }

    public class SomeOtherRequestParameters : IRequestParameters
    {
        public long RequestId { get; set; } = default;
        public CancellationToken CancellationToken { get; set; } = default;
    }
}
