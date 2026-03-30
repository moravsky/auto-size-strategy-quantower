using Moq;
using TradingPlatform.BusinessLayer;

namespace AutoSizeStrategy.Test
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
            _serviceMock
                .Setup(c => c.GetNetPositionQuantity(It.IsAny<IAccount>(), It.IsAny<ISymbol>()))
                .Returns(0);

            _settingsMock.SetupGet(s => s.CurrentAccount).Returns(_accountMock.Object);
            _settingsMock.SetupGet(s => s.RiskPercent).Returns(10.0);
            _settingsMock
                .SetupGet(s => s.MissingStopLossAction)
                .Returns(MissingStopLossAction.Reject);
            _settingsMock.SetupGet(s => s.MinAccountBalanceOverride).Returns(0.0);
            _settingsMock.SetupGet(s => s.MaxContractsMicro).Returns(0);
            _settingsMock.SetupGet(s => s.MaxContractsMini).Returns(0);
            _settingsMock.SetupGet(s => s.DrawdownMode).Returns(DrawdownMode.Static);
            _settingsMock.SetupGet(s => s.CommissionMicro).Returns(0.25);
            _settingsMock.SetupGet(s => s.CommissionMini).Returns(2.50);
            _settingsMock.SetupGet(s => s.AverageSlippageTicks).Returns(1.0);

            var metrics = new Metrics(_settingsMock.Object);
            _contextMock.SetupGet(c => c.Metrics).Returns(metrics);

            // Default Account (Sim)
            _accountMock.SetupGet(a => a.Id).Returns("SimDefault");
            _accountMock.SetupGet(a => a.Balance).Returns(150_000.0);

            // Default Symbol (MES-like)
            _symbolMock.SetupGet(s => s.Id).Returns("MES@CME");
            _symbolMock.SetupGet(s => s.Name).Returns("MES");
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
                var data = new TheoryData<string, double, Dictionary<string, string>, double>
                {
                    // INTRADAY SCENARIO (TPPRO)
                    // Uses 'AutoLiquidateThresholdCurrentValue' to calculate risk.
                    {
                        "TPPRO123456", 150_000, new Dictionary<string, string>
                        {
                            // The trailing drawdown level (High Water Mark - Drawdown Limit)
                            { "AutoLiquidateThresholdCurrentValue", "145500" },
                        },
                        4
                    },
                    // STATIC SCENARIO (Personal/Sim)
                    // No Thresholds provided. Uses raw Balance.
                    { "SimPersonal", 150_000, new Dictionary<string, string>(), 142 },
                    // EDGE CASE: TIGHT TRAILING STOP
                    // Demonstrates what happens when the trailing stop is very close to balance.
                    { "TPPRO149900", 150_000, new Dictionary<string, string>
                    {
                        { "AutoLiquidateThresholdCurrentValue", "149900" },
                    }, 0 }
                };

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
            _accountMock.SetupGet(a => a.Id).Returns(accountId);
            _accountMock.SetupGet(a => a.Balance).Returns(balance);
            _accountMock.SetupGet(a => a.AdditionalInfo).Returns(additionalInfo);

            // Infer the mode from account ID to match what the settings UI would default to
            var tempAccount = _accountMock.Object;
            _settingsMock.SetupGet(s => s.DrawdownMode).Returns(tempAccount.InferDrawdownMode());

            // Stop: 5 pts = 20 ticks. Value: 20 ticks * $5 = $100 risk per contract.
            var request = CreateValidRequest(quantity: 1000, stopDistanceTicks: 20);

            _engine.ProcessRequest(request);

            Assert.Equal(expectedQty, request.Quantity);
        }

        [Fact]
        public void ProcessRequest_EOD_UsesOverride_WhenSet()
        {
            _settingsMock.SetupGet(s => s.MinAccountBalanceOverride).Returns(147_966.0);
            _settingsMock.SetupGet(s => s.DrawdownMode).Returns(DrawdownMode.EndOfDay);
            _accountMock.SetupGet(a => a.Id).Returns("TPT123456");
            _accountMock.SetupGet(a => a.Balance).Returns(151_438.25);
            _accountMock.SetupGet(a => a.AdditionalInfo).Returns(new Dictionary<string, string>());

            // Available = 151438.25 - 147966 = 3472.25
            // Risk 10% = 347.225
            // Stop 20 ticks * $5 = $100/contract
            // Size = 347.225 / 100 = 3.47 -> 3
            var request = CreateValidRequest(quantity: 1000, stopDistanceTicks: 20);

            _engine.ProcessRequest(request);

            Assert.Equal(3, request.Quantity);
        }

        [Fact]
        public void ProcessRequest_NullAccount_LogsErrorAndReturns()
        {
            _settingsMock.SetupGet(s => s.CurrentAccount).Returns((IAccount)null!);
            var request = CreateValidRequest(quantity: 5);

            _engine.ProcessRequest(request);

            _loggerMock.Verify(
                l => l.LogError(It.Is<string>(s => s.Contains("Target account not set"))),
                Times.Once
            );
            Assert.Equal(5, request.Quantity); // unchanged
        }

        [Fact]
        public void ProcessRequest_EOD_NoOverride_LogsErrorAndReturns()
        {
            _settingsMock.SetupGet(s => s.MinAccountBalanceOverride).Returns(0.0);
            _settingsMock.SetupGet(s => s.DrawdownMode).Returns(DrawdownMode.EndOfDay);
            _accountMock.SetupGet(a => a.Id).Returns("TPT123456");
            _accountMock.SetupGet(a => a.Balance).Returns(151_438.25);
            _accountMock.SetupGet(a => a.AdditionalInfo).Returns(new Dictionary<string, string>());

            var request = CreateValidRequest(quantity: 1000, stopDistanceTicks: 20);

            _engine.ProcessRequest(request);

            _loggerMock.Verify(
                l =>
                    l.LogError(
                        It.Is<string>(s =>
                            s.Contains(
                                "End of day drawdown accounts require Minimum Balance Override"
                            )
                        )
                    ),
                Times.Once
            );
            Assert.Equal(1000, request.Quantity);
        }

        #endregion

        #region ProcessRequest ---------------------------------------------

        public static TheoryData<double, Side, double, double, double> ProcessRequestSizingScenarios => new()
        {
            // netPosition, side, requestedQty, stopLossTicks, expectedQty (Max Risk = 150 @ 20 ticks)

            // ENTRIES (NetPos == 0)
            { 0.0, Side.Buy, 1.0, 20.0, 142.0 }, // Magic "Buy 1" -> Upsized to max risk
            { 0.0, Side.Buy, 1000.0, 20.0, 142.0 }, // Oversized Entry -> Capped at max risk

            // ADDING (NetPos > 0)
            { 3.0, Side.Buy, 1.0, 20.0, 139.0 }, // Magic "Buy 1" Top-up -> Upsized to remaining
            { 3.0, Side.Buy, 1000.0, 20.0, 139.0 }, // Oversized Top-up -> Capped at remaining
            { 150.0, Side.Buy, 10.0, 20.0, 0.0 }, // Already maxed -> Cancels to 0

            // REVERSALS (NetPos opposite of order)
            { 10.0, Side.Sell, 11.0, 20.0, 152.0 }, // Reversal Trigger (>10) -> Upsized to max short (10 + 150)
            { 10.0, Side.Sell, 1000.0, 20.0, 152.0 }, // Oversized Flip -> Capped at max short

            // EXITS (Bypasses risk sizing entirely)
            { 10.0, Side.Sell, 5.0, 20.0, 5.0 }, // Partial Exit WITH Stop Loss -> Passes
            { 10.0, Side.Sell, 5.0, 0.0, 5.0 }, // Partial Exit NO Stop Loss -> Passes
            { 10.0, Side.Sell, 10.0, 0.0, 10.0 }, // Full Exit NO Stop Loss -> Passes
        };

        [Theory]
        [MemberData(nameof(ProcessRequestSizingScenarios))]
        public void ProcessRequest_PositionSizing_CalculatesCorrectQuantity(
            double netPosition, Side side, double requestedQty, double stopLossTicks, double expectedQty)
        {
            _serviceMock
                .Setup(c => c.GetNetPositionQuantity(It.IsAny<IAccount>(), It.IsAny<ISymbol>()))
                .Returns(netPosition);

            // Pass 20 as a fallback just to construct the object, we clear it below if 0
            var request = CreateValidRequest(quantity: requestedQty,
                stopDistanceTicks: stopLossTicks > 0 ? stopLossTicks : 20);
            request.Inner.Side = side;

            // 0 Ticks = Simulate no stop loss attached
            if (stopLossTicks <= 0)
                request.StopLossItems.Clear();

            _engine.ProcessRequest(request);

            Assert.Equal(expectedQty, request.Quantity);
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
                l => l.LogInfo(It.Is<string>(s => s.Contains("Insufficient risk budget"))),
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
            _loggerMock.Verify(
                l => l.LogInfo(It.Is<string>(s => s.Contains("Insufficient risk budget"))),
                Times.Never
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
                // Updated the log string to match the engine
                l => l.LogInfo(It.Is<string>(s => s.Contains("bypassing risk math"))),
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
                l => l.LogInfo(It.Is<string>(s => s.Contains("Insufficient risk budget"))),
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

            Assert.Equal(72, request.Quantity);
            _loggerMock.Verify(
                l => l.LogInfo(It.Is<string>(msg => msg.Contains("Changed request"))),
                Times.Once
            );
        }

        [Fact]
        public void ProcessRequest_Logs_WhenRequestAlreadyProcessed()
        {
            var request = CreateValidRequest(quantity: 123, stopDistanceTicks: 40);
            // process request once, so we track request id as processed
            _engine.ProcessRequest(request);

            request.Quantity = 456;
            _engine.ProcessRequest(request);

            Assert.Equal(456, request.Quantity); // passed through size
            _loggerMock.Verify(
                l =>
                    l.LogInfo(
                        It.Is<string>(s =>
                            s.Contains("has already been processed - passing through unchanged")
                        )
                    ),
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
        public void ProcessRequest_Place_HydratesWrapperCorrectly()
        {
            IRequestParameters? capturedWrapper = null;
            var engineMock = new Mock<StrategyEngine>(_contextMock.Object) { CallBase = true };

            // Capture the wrapper, don't execute the real method
            engineMock
                .Setup(e => e.ProcessRequest(It.IsAny<IRequestParameters>()))
                .Callback<IRequestParameters>(p => capturedWrapper = p);

            var sdkParams = new PlaceOrderRequestParameters
            {
                Quantity = 123,
                Price = 5432,
                OrderTypeId = OrderType.Limit,
                Side = Side.Buy,
            };

            engineMock.Object.ProcessRequest(sdkParams);

            // Verify hydration
            Assert.NotNull(capturedWrapper);
            Assert.IsType<PlaceOrderRequestParametersWrapper>(capturedWrapper);

            var wrapper = (PlaceOrderRequestParametersWrapper)capturedWrapper;
            Assert.Equal(123, wrapper.Quantity);
            Assert.Equal(5432, wrapper.Price);
            Assert.Equal(OrderType.Limit, wrapper.OrderTypeId);
            Assert.Equal(Side.Buy, wrapper.Side);
        }

        [Fact]
        public void ProcessRequest_Modify_HydratesWrapperCorrectly()
        {
            IRequestParameters? capturedWrapper = null;
            var engineMock = new Mock<StrategyEngine>(_contextMock.Object) { CallBase = true };

            // Capture the wrapper, don't execute the real method
            engineMock
                .Setup(e => e.ProcessRequest(It.IsAny<IRequestParameters>()))
                .Callback<IRequestParameters>(p => capturedWrapper = p);

            var sdkParams = new ModifyOrderRequestParameters
            {
                Quantity = 123,
                Price = 5432,
                OrderTypeId = OrderType.Limit,
                Side = Side.Buy,
            };

            engineMock.Object.ProcessRequest(sdkParams);

            // Verify hydration
            Assert.NotNull(capturedWrapper);
            Assert.IsType<ModifyOrderRequestParametersWrapper>(capturedWrapper);

            var wrapper = (ModifyOrderRequestParametersWrapper)capturedWrapper;
            Assert.Equal(123, wrapper.Quantity);
            Assert.Equal(5432, wrapper.Price);
            Assert.Equal(OrderType.Limit, wrapper.OrderTypeId);
            Assert.Equal(Side.Buy, wrapper.Side);
        }

        [Fact]
        public void ProcessRequest_ModifyOrder_CalculatesRiskCorrectly()
        {
            _accountMock.SetupGet(a => a.Id).Returns("Static");
            var requestMock = new Mock<IModifyOrderRequestParameters>();

            requestMock.SetupGet(r => r.RequestId).Returns(12345);
            requestMock.SetupGet(r => r.Account).Returns(_accountMock.Object);
            requestMock.SetupGet(r => r.Symbol).Returns(_symbolMock.Object);
            requestMock.SetupGet(r => r.Price).Returns(_symbolMock.Object.Last);
            requestMock.SetupGet(r => r.OrderId).Returns("order67");
            requestMock.SetupGet(r => r.OrderTypeId).Returns(OrderType.Limit);

            var slList = new List<SlTpHolder> { SlTpHolder.CreateSL(20, PriceMeasurement.Offset) };
            requestMock.SetupGet(r => r.StopLossItems).Returns(slList);

            var tpList = new List<SlTpHolder> { SlTpHolder.CreateTP(20, PriceMeasurement.Offset) };
            requestMock.SetupGet(r => r.TakeProfitItems).Returns(tpList);

            requestMock.SetupProperty(r => r.Quantity, 100.0);
            requestMock.SetupProperty(r => r.CancellationToken, CancellationToken.None);

            // Capture the replacement params from CancelReplace
            IPlaceOrderRequestParameters? capturedReplacement = null;
            _serviceMock
                .Setup(s => s.CancelReplace(It.IsAny<string>(), It.IsAny<IPlaceOrderRequestParameters>()))
                .Callback<string, IPlaceOrderRequestParameters>((_, p) => capturedReplacement = p);

            _engine.ProcessRequest(requestMock.Object);

            // Original modify is suppressed via cancellation token
            Assert.True(requestMock.Object.CancellationToken.IsCancellationRequested);

            // CancelReplace fired on the correct order
            _serviceMock.Verify(
                s => s.CancelReplace(
                    It.Is<string>(id => id == "order67"),
                    It.IsAny<IPlaceOrderRequestParameters>()
                ),
                Times.Once
            );

            // Balance 150k, Risk 10% = $15k
            // MES: (20 ticks stop + 1 tick slip) * $5 + $0.50 round-trip comm = $105.50/contract
            // 15000 / 105.50 = 142.18 -> 142 contracts
            Assert.NotNull(capturedReplacement);
            Assert.Equal(142, capturedReplacement.Quantity);

            _loggerMock.Verify(
                l => l.LogInfo(It.Is<string>(s => s.Contains("resizing order"))),
                Times.Once
            );
        }

        [Fact]
        public void ProcessRequest_ModifyOrder_SLTooWide_CancelsWithoutReplace()
        {
            _accountMock.SetupGet(a => a.Id).Returns("Static");
            _accountMock.SetupGet(a => a.Balance).Returns(100.0); // Tiny balance

            var requestMock = new Mock<IModifyOrderRequestParameters>();
            requestMock.SetupGet(r => r.RequestId).Returns(99999);
            requestMock.SetupGet(r => r.Account).Returns(_accountMock.Object);
            requestMock.SetupGet(r => r.Symbol).Returns(_symbolMock.Object);
            requestMock.SetupGet(r => r.Price).Returns(_symbolMock.Object.Last);
            requestMock.SetupGet(r => r.OrderId).Returns("order-to-cancel");
            requestMock.SetupGet(r => r.OrderTypeId).Returns(OrderType.Limit);

            // 500 tick SL = $2500 risk per contract, but only $10 budget (10% of $100)
            var slList = new List<SlTpHolder> { SlTpHolder.CreateSL(500, PriceMeasurement.Offset) };
            requestMock.SetupGet(r => r.StopLossItems).Returns(slList);
            requestMock.SetupProperty(r => r.Quantity, 10.0);

            _engine.ProcessRequest(requestMock.Object);

            Assert.Equal(0, requestMock.Object.Quantity);
            _serviceMock.Verify(
                s => s.Cancel(It.Is<string>(id => id == "order-to-cancel")),
                Times.Once
            );
            _serviceMock.Verify(
                s => s.CancelReplace(It.IsAny<string>(), It.IsAny<IPlaceOrderRequestParameters>()),
                Times.Never
            );
            _loggerMock.Verify(
                l =>
                    l.LogInfo(
                        It.Is<string>(s =>
                            s.Contains("SL too wide") && s.Contains("order-to-cancel")
                        )
                    ),
                Times.Once
            );
        }

        [Theory]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        [InlineData(double.NaN)]
        public void ProcessRequest_NoTickCost_LogsErrorAndCancels(double tickCost)
        {
            _symbolMock.Setup(s => s.GetTickCost(It.IsAny<double>())).Returns(tickCost);
            var request = CreateValidRequest(quantity: 5, stopDistanceTicks: 20);

            _engine.ProcessRequest(request);

            _loggerMock.Verify(
                l => l.LogError(It.Is<string>(s => s.Contains("tick value unavailable"))),
                Times.Once
            );
            Assert.Equal(0, request.Quantity);
        }

        [Theory]
        // (sizeCap, symbolId, netPosition, expectedQty, expectCapLog)
        // calculatedSize = 150 (balance $150k, risk 10%, stop 20 ticks * $5/tick)

        // Cap disabled
        [InlineData(0, "MNQ", 0, 142, false)] // Micro, disabled -> uncapped
        [InlineData(0, "NQ", 0, 136, false)] // Mini, disabled -> uncapped

        // Cap below calculatedSize
        [InlineData(50, "MNQ", 0, 50, true)] // Micro, cap hit -> clamped
        [InlineData(50, "NQ", 0, 50, true)] // Mini, cap hit -> clamped

        // Cap above calculatedSize
        [InlineData(200, "MNQ", 0, 142, false)] // Micro, cap not hit -> uncapped

        // Adding off capped size: cap=10, position=7 -> remaining=3
        [InlineData(10, "MNQ", 7, 3, true)]

        // Position already at cap: cap=10, position=10 -> cancel (qty=0)
        [InlineData(10, "MNQ", 10, 0, true)]
        public void ProcessRequest_MaxContractsCap_Scenarios(
            int sizeCap,
            string symbolName,
            double netPosition,
            double expectedQty,
            bool expectCapLog)
        {
            _settingsMock.SetupGet(s => s.MaxContractsMicro)
                .Returns(symbolName.StartsWith("M") ? sizeCap : 0);
            _settingsMock.SetupGet(s => s.MaxContractsMini)
                .Returns(symbolName.StartsWith("M") ? 0 : sizeCap);
            _symbolMock.SetupGet(s => s.Name).Returns(symbolName);

            _serviceMock
                .Setup(c => c.GetNetPositionQuantity(It.IsAny<IAccount>(), It.IsAny<ISymbol>()))
                .Returns(netPosition);

            var request = CreateValidRequest(quantity: 1000, stopDistanceTicks: 20);

            _engine.ProcessRequest(request);

            Assert.Equal(expectedQty, request.Quantity);
            _loggerMock.Verify(
                l => l.LogInfo(It.Is<string>(s => s.Contains("Capping calculatedSize"))),
                expectCapLog ? Times.Once() : Times.Never()
            );
        }

        [Fact]
        public void ProcessRequest_MaxContractsCap_AllowsFullReversal()
        {

            _settingsMock.SetupGet(s => s.MaxContractsMicro).Returns(10);
            _symbolMock.SetupGet(s => s.Name).Returns("MNQ");

            // Currently Long 5
            _serviceMock
                .Setup(c => c.GetNetPositionQuantity(It.IsAny<IAccount>(), It.IsAny<ISymbol>()))
                .Returns(5.0);

            // User tries to Reverse by Selling 20. 
            var request = CreateValidRequest(quantity: 20, stopDistanceTicks: 20);
            request.Inner.Side = Side.Sell; // Reversal direction

            _engine.ProcessRequest(request);

            // Assert: Max allowed should be 5 (to close long) + 10 (max short cap) = 15.
            Assert.Equal(15, request.Quantity);
        }

        [Fact]
        public void ProcessRequest_IgnoreMissingStopLoss_StillEnforcesMaxCap()
        {
            // Set to Ignore and a hard cap of 10
            _settingsMock.SetupGet(s => s.MissingStopLossAction).Returns(MissingStopLossAction.Ignore);
            _settingsMock.SetupGet(s => s.MaxContractsMicro).Returns(10);
            _symbolMock.SetupGet(s => s.Name).Returns("MNQ");

            // User sends 50 lots with NO stop loss
            var request = CreateValidRequest(quantity: 50, stopDistanceTicks: 20);
            request.StopLossItems.Clear();

            _engine.ProcessRequest(request);

            // The Ignore setting bypassed risk, but the Cap clamped it down to 10
            Assert.Equal(10, request.Quantity);

            _loggerMock.Verify(l => l.LogInfo(It.Is<string>(s => s.Contains("bypassing risk math"))), Times.Once);
            _loggerMock.Verify(l => l.LogInfo(It.Is<string>(s => s.Contains("Capping calculatedSize"))), Times.Once);
        }

        #endregion

        #region Helpers ---------------------------------------------------

        private PlaceOrderRequestParametersWrapper CreateValidRequest(
            double quantity,
            double stopDistanceTicks = 20,
            double profitDistanceTicks = 40
        )
        {
            double currentPrice = _symbolMock.Object.Last;
            var slHolder = SlTpHolder.CreateSL(stopDistanceTicks, PriceMeasurement.Offset);
            var tpHolder = SlTpHolder.CreateTP(profitDistanceTicks, PriceMeasurement.Offset);

            return new PlaceOrderRequestParametersWrapper
            {
                Quantity = quantity,
                Account = _accountMock.Object,
                Symbol = _symbolMock.Object,
                Price = currentPrice,
                StopLossItems = [slHolder],
                TakeProfitItems = [tpHolder],
                OrderTypeId = OrderType.Limit,
            };
        }

        #endregion
    }

    public class SomeOtherRequestParameters : IRequestParameters
    {
        public long RequestId { get; init; } = 0;
        public CancellationToken CancellationToken { get; set; } = CancellationToken.None;
    }
}
