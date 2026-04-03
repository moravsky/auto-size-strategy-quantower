using System;

namespace AutoSizeStrategy
{
    public enum LoggingLevel
    {
        Error,
        Info,
        Verbose,
    }

    public interface IStrategyLogger
    {
        void LogError(string message);
        void LogInfo(string message);
        void LogVerbose(string message);
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
        int InitialStopLossTicks { get; }
        double CommissionMicro { get; }
        double CommissionMini { get; }
        double AverageSlippageTicks { get; }
        double ClutchModeBudget { get; }
        double[] ClutchModeRisk { get; }
        int MaxContractsMicro { get; }
        int MaxContractsMini { get; }
        DrawdownMode DrawdownMode { get; }
        LoggingLevel LoggingLevel { get; }
    }

    public interface IStrategyContext : IDisposable
    {
        IStrategyLogger Logger { get; }
        IStrategySettings Settings { get; }
        ITradingService TradingService { get; }
        Metrics Metrics { get; }
    }

    public record StrategyContext(
        IStrategyLogger Logger,
        IStrategySettings Settings,
        ITradingService TradingService,
        Metrics Metrics
    ) : IStrategyContext
    {
        private bool _disposed;

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