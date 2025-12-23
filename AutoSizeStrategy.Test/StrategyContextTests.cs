using System;
using System.Collections.Generic;
using AutoSizeStrategy;
using Moq;
using TradingPlatform.BusinessLayer;
using Xunit;

namespace AutoSizeStrategy.Tests
{
    public class StrategyContextTests
    {
        private readonly Mock<IStrategyLogger> _loggerMock;
        private readonly Mock<IStrategySettings> _settingsMock;
        private readonly Mock<ITradingService> _serviceMock;

        private readonly Mock<IAccount> _accountMock;
        private readonly Mock<ISymbol> _symbolMock;

        public StrategyContextTests()
        {
            _loggerMock = new Mock<IStrategyLogger>();
            _settingsMock = new Mock<IStrategySettings>();
            _serviceMock = new Mock<ITradingService>();

            _accountMock = new Mock<IAccount>();
            _symbolMock = new Mock<ISymbol>();

            _accountMock.SetupGet(a => a.Id).Returns("Acc1");
            _symbolMock.SetupGet(s => s.Id).Returns("Sym1");
        }

        public static TheoryData<
            string,
            string,
            List<(string Acc, string Sym, Side Side, double Qty)>,
            double
        > NetPositionScenarios
        {
            get
            {
                var data =
                    new TheoryData<
                        string,
                        string,
                        List<(string Acc, string Sym, Side Side, double Qty)>,
                        double
                    >();

                // 1. SIMPLE LONG
                data.Add("Acc1", "ES", new() { ("Acc1", "ES", Side.Buy, 5) }, 5);

                // 2. SIMPLE SHORT
                data.Add("Acc1", "ES", new() { ("Acc1", "ES", Side.Sell, 5) }, -5);

                // 3. FLATTENED (Real World)
                // If I buy 5 and sell 5, the position list is EMPTY.
                data.Add("Acc1", "ES", new() { }, 0);

                // 4. SCALED IN (Real World)
                // If I buy 5, then buy 5, the platform gives me ONE position of 10.
                data.Add("Acc1", "ES", new() { ("Acc1", "ES", Side.Buy, 10) }, 10);

                // 5. IGNORES OTHER SYMBOLS
                data.Add("Acc1", "ES", new() { ("Acc1", "NQ", Side.Buy, 100) }, 0);

                // 6. IGNORES OTHER ACCOUNTS
                data.Add("Acc1", "ES", new() { ("Acc2", "ES", Side.Buy, 100) }, 0);

                return data;
            }
        }

        [Theory]
        [MemberData(nameof(NetPositionScenarios))]
        public void GetNetPositionQuantity_CalculatesCorrectly(
            string targetAcc,
            string targetSym,
            List<(string Acc, string Sym, Side Side, double Qty)> positions,
            double expectedNet
        )
        {
            // Arrange
            var positionMocks = new List<IPosition>();
            foreach (var p in positions)
            {
                positionMocks.Add(CreateMockPosition(p.Acc, p.Sym, p.Side, p.Qty).Object);
            }

            var context = CreateContext(positionMocks);
            var accMock = new Mock<IAccount>();
            accMock.SetupGet(a => a.Id).Returns(targetAcc);
            var symMock = new Mock<ISymbol>();
            symMock.SetupGet(s => s.Id).Returns(targetSym);

            // Act
            double result = context.GetNetPositionQuantity(accMock.Object, symMock.Object);

            // Assert
            Assert.Equal(expectedNet, result);
        }

        private StrategyContext CreateContext(List<IPosition> positions)
        {
            // We use the PRIMARY constructor which is pure and testable
            return new StrategyContext(
                _loggerMock.Object,
                _settingsMock.Object,
                _serviceMock.Object,
                () => positions
            );
        }

        private Mock<IPosition> CreateMockPosition(
            string accId,
            string symId,
            Side side,
            double qty
        )
        {
            var posMock = new Mock<IPosition>();
            var accMock = new Mock<IAccount>();
            accMock.SetupGet(a => a.Id).Returns(accId);

            var symMock = new Mock<ISymbol>();
            symMock.SetupGet(s => s.Id).Returns(symId);

            posMock.SetupGet(p => p.Account).Returns(accMock.Object);
            posMock.SetupGet(p => p.Symbol).Returns(symMock.Object);
            posMock.SetupGet(p => p.Side).Returns(side);
            posMock.SetupGet(p => p.Quantity).Returns(qty);

            return posMock;
        }
    }
}
