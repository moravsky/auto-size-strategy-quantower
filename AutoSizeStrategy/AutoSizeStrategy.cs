using System;
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

            _shutdownCts = new CancellationTokenSource();
            var context = new StrategyContext(this);
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

        private void CoreNewRequest(object sender, RequestEventArgs e)
        {
            try
            {
                strategyEngine.ProcessRequest(e.RequestParameters);
            }
            catch (Exception ex)
            {
                LogError($"CoreNewRequest failed: {ex}");
            }
        }

        private void CoreNewPerformedRequest(object sender, RequestEventArgs e)
        {
            try
            {
                strategyEngine.ProcessRequest(e.RequestParameters);
            }
            catch (Exception ex)
            {
                LogError($"CoreNewPerformedRequest failed: {ex}");
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
