using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
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
        private readonly Mock<ITradingService> _serviceMock;
        private readonly Mock<IAccount> _accountMock;
        private readonly Mock<ISymbol> _symbolMock;
        private readonly StrategyEngine _engine;

        public StrategyEngineTests()
        {
            _loggerMock = new Mock<IStrategyLogger>();
            _contextMock = new Mock<IStrategyContext>();
            _settingsMock = new Mock<IStrategySettings>();
            _serviceMock = new Mock<ITradingService>();
            _accountMock = new Mock<IAccount>();
            _symbolMock = new Mock<ISymbol>();

            _contextMock.SetupGet(c => c.Logger).Returns(_loggerMock.Object);
            _contextMock.SetupGet(c => c.Settings).Returns(_settingsMock.Object);
            _contextMock.SetupGet(c => c.TradingService).Returns(_serviceMock.Object);
            _contextMock
                .Setup(c => c.GetNetPositionQuantity(It.IsAny<IAccount>(), It.IsAny<ISymbol>()))
                .Returns(0);

            _settingsMock.SetupGet(s => s.TargetAccount).Returns(_accountMock.Object);
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
            _symbolMock.Setup(s => s.GetTickCost(It.IsAny<double>())).Returns(5); // $5/tick

            _engine = new StrategyEngine(_contextMock.Object);
        }

        #region Drawdown Logic Tests (Intraday vs EOD vs Static)

        public static TheoryData<
            string,
            double,
            Dictionary<string, string>,
            double
        > DrawdownScenarios
        {
            get
            {
                var data = new TheoryData<string, double, Dictionary<string, string>, double>();

                // INTRADAY SCENARIO (TPPRO)
                // Uses 'AutoLiquidateThresholdCurrentValue' to calculate risk.
                data.Add(
                    "TPPRO123456", // accountId: Matches Intraday Regex
                    150_000, // balance: Current Account Balance
                    new Dictionary<string, string>
                    {
                        // The trailing drawdown level (High Water Mark - Drawdown Limit)
                        { "AutoLiquidateThresholdCurrentValue", "145500" },
                    },
                    4 // expectedQty: (150k - 145.5k) = $4,500 Risk Budget.
                // 10% = $450. Stop $100/contract. Result = 4.
                );

                // END OF DAY SCENARIO (TPT)
                // Uses 'AutoLiquidateThreshold', 'MinAccountBalance', and 'NetPnL'.
                data.Add(
                    "TPT987654", // accountId: Matches EOD Regex
                    150_000, // balance
                    new Dictionary<string, string>
                    {
                        { "AutoLiquidateThreshold", "4500" }, // Static Drawdown Size
                        { "MinAccountBalance", "145500" }, // The Hard Floor
                        { "NetPnL", "0" }, // Today's PnL
                    },
                    4 // expectedQty: Same math as above, but derived differently.
                );

                // STATIC SCENARIO (Personal/Sim)
                // No Thresholds provided. Uses raw Balance.
                data.Add(
                    "SimPersonal", // accountId: Matches NO Regex -> Static Mode
                    150_000, // balance
                    new Dictionary<string, string>(), // No Additional Info needed
                    150 // expectedQty: Risk 10% of 150k = $15,000.
                // Stop $100/contract. Result = 150.
                );

                // EDGE CASE: TIGHT TRAILING STOP
                // Demonstrates what happens when the trailing stop is very close to balance.
                data.Add(
                    "TPPRO149900",
                    150_000,
                    new Dictionary<string, string>
                    {
                        { "AutoLiquidateThresholdCurrentValue", "149900" },
                    },
                    0 // expectedQty: Room is only $100. Risk 10% = $10.
                // Stop is $100. $10 / $100 = 0.1 -> Rounds to 0.
                );

                return data;
            }
        }

        [Theory]
        [MemberData(nameof(DrawdownScenarios))]
        public void ProcessRequest_InfersMode_AndCalculatesCorrectly(
            string accountId,
            double balance,
            Dictionary<string, string> additionalInfo,
            double expectedQty
        )
        {
            // Arrange
            _accountMock.SetupGet(a => a.Id).Returns(accountId);
            _accountMock.SetupGet(a => a.Balance).Returns(balance);
            _accountMock.SetupGet(a => a.AdditionalInfo).Returns(additionalInfo);

            // Stop: 5 pts = 20 ticks. Value: 20 ticks * $5 = $100 risk per contract.
            var request = CreateValidRequest(quantity: 1000, stopDistanceTicks: 20);

            // Act
            _engine.ProcessRequest(request);

            // Assert
            Assert.Equal(expectedQty, request.Quantity);
        }

        #endregion
        #region ProcessRequest ---------------------------------------------

        [Fact]
        public void ProcessRequest_Sdk_PlaceOrderRequestParameters_WrappedCorrectly()
        {
            var request = new PlaceOrderRequestParameters
            {
                AccountId = _accountMock.Object.Id,
                Quantity = 2,
            };
            _engine.ProcessRequest(request);

            Assert.Equal(0, request.Quantity); // set to 0 without SL
        }

        [Fact]
        public void ProcessRequest_Resizes_Correctly()
        {
            _accountMock.SetupGet(a => a.Balance).Returns(2000.0);
            // Risk $200. Stop 5pts ($100/contract). Size = 2.
            var request = CreateValidRequest(quantity: 10, stopDistanceTicks: 20);

            _engine.ProcessRequest(request);

            Assert.Equal(2, request.Quantity); // enforced size
        }

        [Fact]
        public void ProcessRequest_Cancels_NotEnoughForOneContact()
        {
            _accountMock.SetupGet(a => a.Balance).Returns(100.0);
            // Risk $10. Stop 3pts ($15/contract). Size = 10/15 = 0.
            var request = CreateValidRequest(quantity: 1, stopDistanceTicks: 15);

            _engine.ProcessRequest(request);

            Assert.Equal(0, request.Quantity); // cancels the request

            // Verify Log
            _loggerMock.Verify(
                l => l.LogInfo(It.Is<string>(s => s.Contains("Risk too big"))),
                Times.Once
            );
        }

        [Fact]
        public void ProcessRequest_ReduceOnlyWrongSize_PassThrough()
        {
            // Simulate a Long Position of 10 contracts
            _contextMock
                .Setup(c => c.GetNetPositionQuantity(It.IsAny<IAccount>(), It.IsAny<ISymbol>()))
                .Returns(10.0);

            // Create a Sell Request (which is Reduce-Only for a Long position)
            var request = CreateValidRequest(quantity: 3, stopDistanceTicks: 20);
            request.Inner.Side = Side.Sell;

            _engine.ProcessRequest(request);

            // Quantity should remain 3, even though risk calc would resize it to 150
            Assert.Equal(3, request.Quantity);

            // Verify Log
            _loggerMock.Verify(
                l => l.LogInfo(It.Is<string>(s => s.Contains("is Reduce-Only"))),
                Times.Once
            );
        }

        [Fact]
        public void ProcessRequest_ReduceOnlyNoStop_PassThrough()
        {
            // Simulate a Long Position of 10 contracts
            _contextMock
                .Setup(c => c.GetNetPositionQuantity(It.IsAny<IAccount>(), It.IsAny<ISymbol>()))
                .Returns(10.0);

            // Create a Sell Request (which is Reduce-Only for a Long position)
            var request = CreateValidRequest(quantity: 150, stopDistanceTicks: 20);
            request.Inner.Side = Side.Sell;
            request.StopLossItems.Clear();

            _engine.ProcessRequest(request);

            // Quantity should remain 150, even though process request would set it to 0
            Assert.Equal(150, request.Quantity);

            // Verify Log
            _loggerMock.Verify(
                l => l.LogInfo(It.Is<string>(s => s.Contains("is Reduce-Only"))),
                Times.Once
            );
        }

        [Fact]
        public void ProcessRequest_Cancels_WhenNoStopLossReject()
        {
            _settingsMock
                .SetupGet(s => s.MissingStopLossAction)
                .Returns(MissingStopLossAction.Reject);

            var request = CreateValidRequest(quantity: 10, stopDistanceTicks: 20);
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

            var request = CreateValidRequest(quantity: 10, stopDistanceTicks: 20);
            request.StopLossItems.Clear();

            _engine.ProcessRequest(request);

            Assert.Equal(10, request.Quantity);
            _loggerMock.Verify(
                l => l.LogInfo(It.Is<string>(s => s.Contains("passing through unchanged"))),
                Times.Once
            );
        }

        [Fact]
        public void ProcessRequest_HugeStopLoss_ResultsInZeroQuantity()
        {
            _accountMock.SetupGet(a => a.Id).Returns("PersonalAccount");
            _accountMock.SetupGet(a => a.Balance).Returns(10_000); // Risk 10% = $1,000

            // User sets a massive stop loss (e.g., 500 points on ES = 2000 ticks)
            // 2000 ticks * $5 = $10,000 risk per contract.
            // Budget is only $1,000.
            // Result: 0.1 contracts -> 0.
            var request = CreateValidRequest(quantity: 1, stopDistanceTicks: 2000);

            _engine.ProcessRequest(request);

            Assert.Equal(0, request.Quantity);
            _loggerMock.Verify(
                l => l.LogInfo(It.Is<string>(s => s.Contains("Risk too big"))),
                Times.Once
            );
        }

        [Fact]
        public void ProcessRequest_DoesNothing_WhenNotPlaceOrder()
        {
            var nonPlaceRequest = new SomeOtherRequestParameters(); // any non‑PlaceOrder type

            _engine.ProcessRequest(nonPlaceRequest);

            // No changes expected – no exception must be thrown
        }

        [Fact]
        public void ProcessRequest_LogsEnforceInfo_WhenQuantityAdjusted()
        {
            // Quantity is 1 -> should be changed to 75 by the strategy
            var request = CreateValidRequest(quantity: 1, stopDistanceTicks: 40);

            _engine.ProcessRequest(request);

            Assert.Equal(75, request.Quantity);
            _loggerMock.Verify(
                l => l.LogInfo(It.Is<string>(msg => msg.Contains("Changed request"))),
                Times.Once
            );
        }

        [Fact]
        public void ProcessRequest_Logs_WhenRequestAlreadyProcessed()
        {
            var request = new PlaceOrderRequestParameters
            {
                AccountId = _accountMock.Object.Id,
                Quantity = 0,
            };

            _engine.ProcessRequest(request);
            _engine.ProcessRequest(request);

            Assert.Equal(0, request.Quantity); // passed through size
            _loggerMock.Verify(
                l => l.LogInfo(It.Is<string>(s => s.Contains("passing through unchanged"))),
                Times.Once
            );
        }

        [Fact]
        public void ProcessRequest_HydratesWrapperProperties_FromInnerSdkObject()
        {
            // Create a "Raw" SDK parameter object (simulating what Quantower sends)
            // We intentionally leave Account/Symbol null to test null-safety of the wrapper
            var innerParams = new PlaceOrderRequestParameters { Quantity = 10, Price = 5000 };

            // Pass it through the Factory
            var wrapper = (PlaceOrderRequestParametersWrapper)
                RequestParametersWrapper.Create(innerParams);

            // Properties should NOT be null (Wrapped safely)
            Assert.NotNull(wrapper.Account);
            Assert.NotNull(wrapper.Symbol);

            // Types should be our Wrappers
            Assert.IsType<AccountWrapper>(wrapper.Account);
            Assert.IsType<SymbolWrapper>(wrapper.Symbol);

            // Data integrity
            Assert.Equal(innerParams.RequestId, wrapper.RequestId);
            Assert.Equal(5000, wrapper.Price);

            // Verify modifying the wrapper updates the inner SDK object
            wrapper.Quantity = 50;
            Assert.Equal(50, innerParams.Quantity);
        }

        [Fact]
        public void ProcessRequest_Hydration_WrapsAndValidatesModifyOrder()
        {
            var sdkParams = new ModifyOrderRequestParameters
            {
                AccountId = _accountMock.Object.Id,
                Quantity = 100, // Requesting 100
                Price = 5000,
            };

            _settingsMock
                .SetupGet(s => s.MissingStopLossAction)
                .Returns(MissingStopLossAction.Reject);

            _engine.ProcessRequest(sdkParams);

            // The engine should have hydrated the wrapper, seen the empty StopLoss list, and set Qty to 0
            Assert.Equal(0, sdkParams.Quantity);
            // Price should remain unchanged
            Assert.Equal(5000, sdkParams.Price);

            _loggerMock.Verify(
                l => l.LogInfo(It.Is<string>(s => s.Contains("cancelled: stop loss required"))),
                Times.Once
            );
        }

        [Fact]
        public void ProcessRequest_ModifyOrder_CalculatesRiskCorrectly()
        {
            _accountMock.SetupGet(a => a.Id).Returns("Static"); // Intraday account
            var requestMock = new Mock<IModifyOrderRequestParameters>();

            requestMock.SetupGet(r => r.RequestId).Returns(12345);
            requestMock.SetupGet(r => r.Account).Returns(_accountMock.Object);
            requestMock.SetupGet(r => r.AccountId).Returns(_accountMock.Object.Id);
            requestMock.SetupGet(r => r.Symbol).Returns(_symbolMock.Object);
            requestMock.SetupGet(r => r.Price).Returns(_symbolMock.Object.Last);
            requestMock.SetupGet(r => r.OrderTypeId).Returns(OrderType.Limit);

            var slList = new List<SlTpHolder> { SlTpHolder.CreateSL(20, PriceMeasurement.Offset) };
            requestMock.SetupGet(r => r.StopLossItems).Returns(slList);

            // Setup the "Requested" Quantity
            // We use SetupProperty so the Engine can update it
            requestMock.SetupProperty(r => r.Quantity, 100);

            _engine.ProcessRequest(requestMock.Object);

            // Balance 150k.
            // Risk 10% = $15K.
            // SL 20 ticks * $5/tick = $100 risk/contract.
            // Expected Size = 150 contracts.
            Assert.Equal(150, requestMock.Object.Quantity);

            _loggerMock.Verify(
                l => l.LogInfo(It.Is<string>(s => s.Contains("Changed request"))),
                Times.Once
            );
        }

        #endregion

        #region ProcessFailSafe -------------------------------------------

        [Fact]
        public async Task ProcessFailSafe_EntryRightSize_PassThrough()
        {
            // Start with 20K balance
            _accountMock.SetupGet(a => a.Balance).Returns(10_000.0);

            // Create a Buy Order
            // Risk budget = 1k. Risk = 1k / 5$ per tick (incorrect MES) / 20 ticks = 10 contracts
            var order = CreateMockOrder("id1", 10);
            order.SetupGet(o => o.Side).Returns(Side.Sell);
            order
                .SetupGet(o => o.StopLossItems)
                .Returns([SlTpHolder.CreateSL(20, PriceMeasurement.Offset)]);

            await _engine.ProcessFailSafe(order.Object);

            // Should NOT call Cancel
            _serviceMock.Verify(s => s.Cancel(It.IsAny<IOrder>()), Times.Never);
        }

        [Fact]
        public async Task ProcessFailSafe_Cancels_WhenQuantityMismatch()
        {
            var order = CreateMockOrder(id: "456", qty: 5);
            order
                .SetupGet(o => o.StopLossItems)
                .Returns([SlTpHolder.CreateSL(20, PriceMeasurement.Offset)]);

            await _engine.ProcessFailSafe(order.Object);

            // Correct size: 150K * 10% = 15K / 5$ per tick (incorrect MES) / 20 ticks = 150
            var calculatedSize = 150;
            _loggerMock.Verify(
                l =>
                    l.LogInfo(
                        It.Is<string>(s =>
                            s.Contains(
                                $"Cancelling Order {order.Object.Id}. Size is {order.Object.TotalQuantity}, must be {calculatedSize}."
                            )
                        )
                    ),
                Times.Once
            );
            _serviceMock.Verify(s => s.Cancel(It.Is<IOrder>(i => i.Id.Equals("456"))), Times.Once);
        }

        [Fact]
        public async Task ProcessFailSafe_DoesNothing_WhenOrderClosed()
        {
            var order = new Mock<IOrder>();
            // Closed order – should skip
            order.SetupGet(o => o.Status).Returns(OrderStatus.Cancelled);

            await _engine.ProcessFailSafe(order.Object);

            _loggerMock.VerifyNoOtherCalls();
            order.VerifyGet(o => o.Status, Times.AtLeastOnce);
        }

        [Fact]
        public async Task ProcessFailSafe_ReduceOnlyWrongSize_PassThrough()
        {
            // Simulate a Short Position of -10 contracts
            _contextMock
                .Setup(c => c.GetNetPositionQuantity(It.IsAny<IAccount>(), It.IsAny<ISymbol>()))
                .Returns(-10.0);

            // Create a Buy Order (Reduce-Only for Short)
            var order = CreateMockOrder("id_reduce", 5);
            order.SetupGet(o => o.Side).Returns(Side.Buy);
            // Simulate that the size is "wrong" according to risk to ensure it would be cancelled if not reduce-only
            // (e.g. Risk calc might want 1 contract, but order is 5)

            await _engine.ProcessFailSafe(order.Object);

            // Should NOT call Cancel
            _serviceMock.Verify(s => s.Cancel(It.IsAny<IOrder>()), Times.Never);
        }

        [Fact]
        public async Task ProcessFailSafe_ReduceOnlyNoStop_PassThrough()
        {
            // Simulate a Short Position of -10 contracts
            _contextMock
                .Setup(c => c.GetNetPositionQuantity(It.IsAny<IAccount>(), It.IsAny<ISymbol>()))
                .Returns(-10.0);

            // Create a Buy Order (Reduce-Only for Short)
            var order = CreateMockOrder("id_reduce", 5);
            order.SetupGet(o => o.Side).Returns(Side.Buy);
            order.SetupGet(o => o.StopLossItems).Returns([]);
            // Simulate that the size is "wrong" according to risk to ensure it would be cancelled if not reduce-only
            // (e.g. Risk calc might want 1 contract, but order is 5)

            await _engine.ProcessFailSafe(order.Object);

            // Should NOT call Cancel
            _serviceMock.Verify(s => s.Cancel(It.IsAny<IOrder>()), Times.Never);
        }

        #endregion

        #region Helpers ---------------------------------------------------

        private PlaceOrderRequestParametersWrapper CreateValidRequest(
            double quantity,
            double stopDistanceTicks
        )
        {
            double currentPrice = _symbolMock.Object.Last;
            var slTpHolder = SlTpHolder.CreateSL(stopDistanceTicks, PriceMeasurement.Offset);

            return new PlaceOrderRequestParametersWrapper
            {
                Quantity = quantity,
                Account = _accountMock.Object,
                AccountId = _accountMock.Object.Id,
                Symbol = _symbolMock.Object,
                Price = currentPrice,
                StopLossItems = [slTpHolder],
                OrderTypeId = OrderType.Limit,
            };
        }

        private Mock<IOrder> CreateMockOrder(string id, double qty)
        {
            var order = new Mock<IOrder>();
            order.SetupGet(o => o.Account).Returns(_accountMock.Object);
            order.SetupGet(o => o.Symbol).Returns(_symbolMock.Object);
            order.SetupGet(o => o.Status).Returns(OrderStatus.Opened);
            order.SetupGet(o => o.TotalQuantity).Returns(qty);
            order.SetupGet(o => o.Id).Returns(id);
            order.SetupGet(o => o.OrderTypeId).Returns(OrderType.Limit);
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
