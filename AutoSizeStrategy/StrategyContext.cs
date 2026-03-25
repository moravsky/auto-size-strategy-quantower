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
        IAccount CurrentAccount { get; }
        double RiskPercent { get; }
        MissingStopLossAction MissingStopLossAction { get; }
        double MinAccountBalanceOverride { get; }
        int MinimumStopLossTicks { get; }
        double CommissionMicro { get; }
        double CommissionMini { get; }
        double AverageSlippageTicks { get; }
        double ClutchModeBudget { get; }
        double[] ClutchModeRisk { get; }
        int MaxContractsMicro { get; }
        int MaxContractsMini { get; }
    }

    public interface IStrategyContext : IDisposable
    {
        IStrategyLogger Logger { get; }
        IStrategySettings Settings { get; }
        ITradingService TradingService { get; }
        Metrics Metrics { get; }
        double GetNetPositionQuantity(IAccount account, ISymbol symbol);
    }

    public record StrategyContext(
        IStrategyLogger Logger,
        IStrategySettings Settings,
        ITradingService TradingService,
        Metrics Metrics,
        Func<IEnumerable<IPosition>> PositionProvider
    ) : IStrategyContext
    {
        private bool _disposed = false;

        // TODO: V3: Introduce DI container for pluggable logic
        public StrategyContext(AutoSizeStrategy autoSizeStrategy, Metrics metrics)
            : this(
                Logger: autoSizeStrategy,
                Settings: autoSizeStrategy,
                new TradingService(autoSizeStrategy),
                Metrics: metrics,
                () => Core.Instance.Positions.Select(p => new PositionWrapper(p))
            ) { }

        public double GetNetPositionQuantity(IAccount account, ISymbol symbol)
        {
            var position = PositionProvider()
                .FirstOrDefault(p => p.Account.Id == account.Id && p.Symbol.Id == symbol.Id);

            if (position == null)
                return 0;

            return position.Side == Side.Buy ? position.Quantity : -position.Quantity;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                TradingService.Dispose();
                _disposed = true;
            }

            GC.SuppressFinalize(this);
        }
    }
}
