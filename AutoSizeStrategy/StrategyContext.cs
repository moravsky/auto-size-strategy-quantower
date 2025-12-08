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

    public interface IStrategyContext
    {
        IStrategyLogger Logger { get; }
        IStrategySettings Settings { get; }
    }

    public record StrategyContext(IStrategyLogger Logger, IStrategySettings Settings)
        : IStrategyContext
    {
        public StrategyContext(AutoSizeStrategy autoSizeStrategy)
            : this(autoSizeStrategy, autoSizeStrategy) { }
    }
}
