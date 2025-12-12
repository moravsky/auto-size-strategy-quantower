using System;

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
    }

    public record StrategyContext(
        IStrategyLogger Logger,
        IStrategySettings Settings,
        IOrderKiller OrderKiller
    ) : IStrategyContext
    {
        private bool _disposed = false;

        // TODO: V3: Introducde DI container for pluggable logic
        public StrategyContext(AutoSizeStrategy autoSizeStrategy)
            : this(autoSizeStrategy, autoSizeStrategy, new OrderKiller(autoSizeStrategy)) { }

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
