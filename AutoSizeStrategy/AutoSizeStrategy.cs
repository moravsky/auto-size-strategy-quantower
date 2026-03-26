using System;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using TradingPlatform.BusinessLayer;
using TradingPlatform.BusinessLayer.Utils;

namespace AutoSizeStrategy
{
    public partial class AutoSizeStrategy
        : Strategy,
            ICurrentAccount,
            IStrategyLogger,
            IStrategySettings,
            IDisposable
    {
        private bool _disposed;
        private StrategyEngine _strategyEngine;
        private Metrics _metrics;
        private CancellationTokenSource _shutdownCts;

        // Backing fields for metric gauges.
        // Volatile.Read/Write used for thread safety — 'volatile double' is not valid C#.
        private double _mRiskPercent;
        private double _mRiskCapital = double.NaN;
        private double _mTradesToClutch = double.NaN;
        private double _mTradesToBust = double.NaN;
        private readonly object _metricsLock = new();
        private const int HeartbeatPeriodMs = 1000;

        public AutoSizeStrategy()
        {
            this.Name = "AutoSizeStrategy42";
            this.Description = "Sizes orders for ALL symbols according to risk parameters.";
        }

        protected override void OnRun()
        {
            if (CurrentAccount == null)
            {
                LogError("No target account configured - strategy cannot run");
                return;
            }

            if (
                DrawdownMode == DrawdownMode.EndOfDay
                && MinAccountBalanceOverride == 0
            )
            {
                LogError("End of day drawdown accounts require Minimum Balance Override");
                return;
            }

            _shutdownCts = new CancellationTokenSource();
            this._metrics = new Metrics(this);
            var context = new StrategyContext(this, this._metrics);
            this._strategyEngine = new StrategyEngine(context);

            Core.NewRequest += this.CoreNewRequest;
            Core.NewPerformedRequest += this.CoreNewPerformedRequest;
            Core.OrderRemoved += this.CoreOrderRemoved;

            UpdateMetrics();
            StartHeartbeat(_shutdownCts.Token);
        }

        protected override void OnCreated()
        {
            base.OnCreated();
            InitializeSettings();
        }

        protected override void OnInitializeMetrics(Meter meter)
        {
            base.OnInitializeMetrics(meter);

            meter.CreateObservableGauge(
                "target-risk-pct",
                () => Volatile.Read(ref _mRiskPercent),
                unit: "%",
                description: "Target Risk (%)"
            );
            meter.CreateObservableGauge(
                "risk-capital",
                () => Volatile.Read(ref _mRiskCapital),
                unit: "$",
                description: "Risk Capital ($)"
            );
            meter.CreateObservableGauge(
                "trades-to-clutch",
                () => Volatile.Read(ref _mTradesToClutch),
                description: "Trades to Clutch"
            );
            meter.CreateObservableGauge(
                "trades-to-bust",
                () => Volatile.Read(ref _mTradesToBust),
                description: "Trades to Bust"
            );
        }

        private void StartHeartbeat(CancellationToken token)
        {
            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(HeartbeatPeriodMs, token);

                        if (!token.IsCancellationRequested)
                        {
                            UpdateMetrics();
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        // Normal shutdown
                        break;
                    }
                    catch (Exception ex)
                    {
                        LogError($"Metrics timer error: {ex.Message}");
                    }
                }
            }, token);
        }

        private void UpdateMetrics()
        {
            lock (_metricsLock)
            {
                try
                {
                    Volatile.Write(ref _mRiskPercent, RiskPercent);

                    if (_metrics == null)
                        return;

                    var m = _metrics.GetAccountMetrics();

                    Volatile.Write(ref _mRiskCapital, m.RiskCapital ?? double.NaN);
                    Volatile.Write(ref _mTradesToClutch, m.TradesToClutchMode ?? double.NaN);
                    Volatile.Write(ref _mTradesToBust, m.TradesToBust ?? double.NaN);
                }
                catch (Exception ex)
                {
                    LogError($"UpdateMetrics failed: {ex.Message}");
                }
            }
        }

        private void CoreNewRequest(object sender, RequestEventArgs e)
        {
            try
            {
                _strategyEngine.ProcessRequest(e.RequestParameters);
                UpdateMetrics();
            }
            catch (Exception ex)
            {
                LogError($"ProcessRequest failed: {ex}");
            }
        }

        private void CoreNewPerformedRequest(object sender, RequestEventArgs e)
        {
            try
            {
                _strategyEngine.ReportCompletedRequest(e.RequestParameters);
            }
            catch (Exception ex)
            {
                LogError($"ReportCompletedRequest failed: {ex}");
            }
        }

        private async void CoreOrderRemoved(Order order)
        {
            try
            {
                var cts = _shutdownCts;
                if (_disposed || cts == null)
                    return;

                await Task.Run(() => _strategyEngine.ReportCancelledOrder(order.Id), cts.Token);
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException)
            {
                // Graceful shutdown, safely ignore
            }
            catch (Exception ex)
            {
                LogError($"CoreOrderRemoved failed for order {order.Id}: {ex}");
            }
        }

        protected override void OnStop()
        {
            var cts = _shutdownCts;
            if (cts == null)
                return;

            Core.NewRequest -= this.CoreNewRequest;
            Core.NewPerformedRequest -= this.CoreNewPerformedRequest;
            Core.OrderRemoved -= this.CoreOrderRemoved;

            _shutdownCts.Cancel();
            _shutdownCts.Dispose();
            _shutdownCts = null;
            _strategyEngine?.Dispose();
            _strategyEngine = null;
            _metrics = null;
        }

        protected override void OnRemove() => Dispose();

        void IDisposable.Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _disposed = true;
                OnStop();
                base.Dispose();
            }
        }

        public void LogError(string message) => Log(message, StrategyLoggingLevel.Error);

        public void LogInfo(string message) => Log(message);
    }
}
