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
        // No more Mock<AutoSizeStrategy>!
        private readonly Mock<IStrategyLogger> _loggerMock;
        private readonly Mock<IStrategySettings> _settingsMock;
        private readonly Mock<IOrderKiller> _killerMock;

        private readonly Mock<IAccount> _accountMock;
        private readonly Mock<ISymbol> _symbolMock;

        public StrategyContextTests()
        {
            _loggerMock = new Mock<IStrategyLogger>();
            _settingsMock = new Mock<IStrategySettings>();
            _killerMock = new Mock<IOrderKiller>();

            _accountMock = new Mock<IAccount>();
            _symbolMock = new Mock<ISymbol>();

            _accountMock.SetupGet(a => a.Id).Returns("Acc1");
            _symbolMock.SetupGet(s => s.Id).Returns("Sym1");
        }

        private StrategyContext CreateContext(List<IPosition> positions)
        {
            // We use the PRIMARY constructor which is pure and testable
            return new StrategyContext(
                _loggerMock.Object,
                _settingsMock.Object,
                _killerMock.Object,
                () => positions
            );
        }

        [Fact]
        public void GetNetPosition_NoPositions_ReturnsZero()
        {
            var context = CreateContext(new List<IPosition>());
            double result = context.GetNetPositionQuantity(_accountMock.Object, _symbolMock.Object);
            Assert.Equal(0, result);
        }

        [Fact]
        public void GetNetPosition_LongPosition_ReturnsPositiveQuantity()
        {
            var pos = CreateMockPosition("Acc1", "Sym1", Side.Buy, 10);
            var context = CreateContext(new List<IPosition> { pos.Object });

            double result = context.GetNetPositionQuantity(_accountMock.Object, _symbolMock.Object);

            Assert.Equal(10, result);
        }

        [Fact]
        public void GetNetPosition_ShortPosition_ReturnsNegativeQuantity()
        {
            var pos = CreateMockPosition("Acc1", "Sym1", Side.Sell, 5);
            var context = CreateContext(new List<IPosition> { pos.Object });

            double result = context.GetNetPositionQuantity(_accountMock.Object, _symbolMock.Object);

            Assert.Equal(-5, result);
        }

        [Fact]
        public void GetNetPosition_IgnoresDifferentAccount()
        {
            var pos = CreateMockPosition("Acc2", "Sym1", Side.Buy, 100);
            var context = CreateContext(new List<IPosition> { pos.Object });

            double result = context.GetNetPositionQuantity(_accountMock.Object, _symbolMock.Object);

            Assert.Equal(0, result);
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
