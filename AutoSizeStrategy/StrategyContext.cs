using System;
using System.Collections.Generic;
using System.Linq;
using TradingPlatform.BusinessLayer;

namespace AutoSizeStrategy
{
    public interface IStrategyLogger
    {
        void LogError(string message);
        void LogInfo(string message);
    }

    public enum MissingStopLossAction
    {
        Reject,
        Ignore,
    }

    public interface IStrategySettings
    {
        double RiskPercent { get; }
        MissingStopLossAction MissingStopLossAction { get; }
    }

    public interface IStrategyContext : IDisposable
    {
        IStrategyLogger Logger { get; }
        IStrategySettings Settings { get; }
        IOrderKiller OrderKiller { get; }
        double GetNetPositionQuantity(IAccount account, ISymbol symbol);
    }

    public record StrategyContext(
        IStrategyLogger Logger,
        IStrategySettings Settings,
        IOrderKiller OrderKiller,
        Func<IEnumerable<IPosition>> PositionProvider
    ) : IStrategyContext
    {
        private bool _disposed = false;

        // TODO: V3: Introducde DI container for pluggable logic
        public StrategyContext(AutoSizeStrategy autoSizeStrategy)
            : this(
                Logger: autoSizeStrategy,
                Settings: autoSizeStrategy,
                new OrderKiller(autoSizeStrategy),
                () => Core.Instance.Positions.Select(p => new PositionWrapper(p))
            ) { }

        public double GetNetPositionQuantity(IAccount account, ISymbol symbol)
        {
            var position = PositionProvider()
                .FirstOrDefault(p => p.Account.Id == account.Id && p.Symbol.Id == symbol.Id);

            if (position == null)
                return 0;

            // Convert to signed quantity: Buy is positive, Sell is negative
            return position.Side == Side.Buy ? position.Quantity : -position.Quantity;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                OrderKiller.Dispose();
                _disposed = true;
            }

            GC.SuppressFinalize(this);
        }
    }
}
