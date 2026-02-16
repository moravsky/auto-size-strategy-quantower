using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TradingPlatform.BusinessLayer;
using TradingPlatform.BusinessLayer.Utils;

namespace AutoSizeStrategy
{
    public class AutoSizeStrategy
        : Strategy,
            ICurrentAccount,
            IStrategyLogger,
            IStrategySettings,
            IDisposable
    {
        private bool _disposed;
        private StrategyEngine strategyEngine;
        private CancellationTokenSource _shutdownCts;

        [InputParameter("Account")]
        public Account CurrentAccount { get; set; } = Core.Accounts.FindTargetAccount();

        IAccount IStrategySettings.CurrentAccount =>
            CurrentAccount != null ? new AccountWrapper(CurrentAccount) : null;

        [InputParameter("Risk Percent", minimum: 0.1, maximum: 100.0, increment: 0.1)]
        public double RiskPercent { get; set; } = 8.0;

        [InputParameter(
            "Action on Missing Stop Loss",
            variants: new object[]
            {
                "Reject",
                MissingStopLossAction.Reject,
                "Ignore",
                MissingStopLossAction.Ignore,
            }
        )]
        public MissingStopLossAction MissingStopLossAction { get; set; } =
            MissingStopLossAction.Reject;

        [InputParameter("Minimum Account Balance", minimum: 0.0, increment: .01)]
        public double MinAccountBalanceOverride { get; set; } = 0.0;

        public AutoSizeStrategy()
        {
            this.Name = "AutoSizeStrategy42";
            this.Description =
                "Size orders for ALL symbols and accounts according to risk parameters.";
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

        protected override void OnRemove()
        {
            Dispose();
        }

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

        private async void OnOrderAdded(Order order)
        {
            var cts = _shutdownCts;

            if (_disposed || cts == null)
                return;

            // TODO: this is pretty complex and untestable (especially wrapping), consider moving to StrategyEngine
            // TODO: filter out cancel orders somewhere
            try
            {
                var orderWrapper = new OrderWrapper(order);
                await Task.Run(() => strategyEngine.ProcessOrder(orderWrapper), cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown - ignore
            }
            catch (ObjectDisposedException)
            {
                // Expected if cts was disposed mid-execution - ignore
            }
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

            // TODO: this is pretty complex and untestable, consider moving to StrategyEngine
            try
            {
                await Task.Run(() => strategyEngine.ReportCancelledOrder(order.Id), cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown - ignore
            }
            catch (ObjectDisposedException)
            {
                // Expected if cts was disposed mid-execution - ignore
            }
            catch (Exception ex)
            {
                LogError($"CoreOrderRemoved failed for order {order.Id}: {ex}");
            }
        }

        public void LogError(string message)
        {
            Log(message, StrategyLoggingLevel.Error);
        }

        public void LogInfo(string message)
        {
            Log(message, StrategyLoggingLevel.Info);
        }
    }
}
