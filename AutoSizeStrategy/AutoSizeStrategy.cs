using System;
using System.Collections.Generic;
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
        private StrategyEngine strategyEngine;
        private Metrics metrics;
        private CancellationTokenSource _shutdownCts;

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
                ((IStrategySettings)this).CurrentAccount.InferDrawdownMode()
                    == DrawdownMode.EndOfDay
                && MinAccountBalanceOverride == 0
            )
            {
                LogError($"End of day drawdown accounts require Minimum Balance Override");
                return;
            }

            _shutdownCts = new CancellationTokenSource();
            this.metrics = new Metrics(this);
            var context = new StrategyContext(this, this.metrics);
            this.strategyEngine = new StrategyEngine(context);

            Core.NewRequest += this.CoreNewRequest;
            Core.NewPerformedRequest += this.CoreNewPerformedRequest;
            Core.OrderAdded += OnOrderAdded;
            Core.OrderRemoved += this.CoreOrderRemoved;
        }

        protected override void OnCreated()
        {
            base.OnCreated();
            InitializeSettings();
        }

        [Obsolete("Use OnInitializeMetrics()")]
        protected override List<StrategyMetric> OnGetMetrics()
        {
            var result = base.OnGetMetrics();

            try
            {
                if (metrics == null)
                    return result;

                var m = metrics.GetAccountMetrics();

                result.Add(
                    new StrategyMetric
                    {
                        Name = "Risk Percent",
                        FormattedValue = $"{(RiskPercent / 100.0):P2}",
                    }
                );
                result.Add(
                    new StrategyMetric
                    {
                        Name = "Drawdown Remaining",
                        FormattedValue =
                            m.DrawdownRemaining != null ? $"${m.DrawdownRemaining:F2}" : "N/A",
                    }
                );
                var tradesToClutch =
                    m.TradesToClutchMode != null ? m.TradesToClutchMode.ToString() : "N/A";
                result.Add(
                    new StrategyMetric
                    {
                        Name = "Trades to Clutch",
                        FormattedValue = tradesToClutch,
                    }
                );
                var tradesToBust = m.TradesToBust != null ? m.TradesToBust.ToString() : "N/A";
                result.Add(
                    new StrategyMetric { Name = "Trades to Bust", FormattedValue = tradesToBust }
                );
            }
            catch (Exception ex)
            {
                LogError($"OnGetMetrics failed: {ex.Message}");
            }

            return result;
        }

        private void CoreNewRequest(object sender, RequestEventArgs e)
        {
            try
            {
                strategyEngine.ProcessRequest(e.RequestParameters);
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
                strategyEngine.ReportCompletedRequest(e.RequestParameters);
            }
            catch (Exception ex)
            {
                LogError($"ReportCompletedRequest failed: {ex}");
            }
        }

        private async void OnOrderAdded(Order order)
        {
            var cts = _shutdownCts;
            if (_disposed || cts == null)
                return;

            try
            {
                var orderWrapper = new OrderWrapper(order);
                await Task.Run(() => strategyEngine.ProcessOrder(orderWrapper), cts.Token);
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                LogError($"OnOrderAdded failed for order {order.Id}: {ex}");
            }
        }

        private async void CoreOrderRemoved(Order order)
        {
            var cts = _shutdownCts;
            if (_disposed || cts == null)
                return;

            try
            {
                await Task.Run(() => strategyEngine.ReportCancelledOrder(order.Id), cts.Token);
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
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
            Core.OrderAdded -= OnOrderAdded;
            Core.NewPerformedRequest -= this.CoreNewPerformedRequest;
            Core.OrderRemoved -= this.CoreOrderRemoved;

            _shutdownCts.Cancel();
            _shutdownCts.Dispose();
            _shutdownCts = null;
            strategyEngine?.Dispose();
            strategyEngine = null;
            metrics = null;
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

        public void LogInfo(string message) => Log(message, StrategyLoggingLevel.Info);
    }
}
