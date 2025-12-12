using System;
using TradingPlatform.BusinessLayer;
using TradingPlatform.BusinessLayer.Utils;

namespace AutoSizeStrategy
{
    public class AutoSizeStrategy : Strategy, IStrategyLogger, IStrategySettings, IDisposable
    {
        private bool _disposed;
        private string _instanceId;
        private StrategyEngine strategyEngine;

        [InputParameter("Risk Percent", minimum: 1.0, maximum: 100.0, increment: 1.0)]
        public double RiskPercent { get; set; } = 10.0;

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

        public AutoSizeStrategy()
        {
            this.Name = "AutoSizeStrategy42";
            this.Description =
                "Size orders for ALL symbols and accounts according to risk parameters.";
            var context = new StrategyContext(this);
            this.strategyEngine = new StrategyEngine(context);
        }

        protected override void OnCreated()
        {
            _instanceId = Guid.NewGuid().ToString().Substring(0, 4).ToUpper();
        }

        protected override void OnRun()
        {
            Core.OrderAdded += OnOrderAdded;
            Core.NewRequest += this.CoreNewRequest;
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
                throw;
            }
        }

        protected override void OnStop()
        {
            Dispose();
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
                Core.OrderAdded -= OnOrderAdded;
                Core.NewRequest -= this.CoreNewRequest;
                Core.OrderRemoved -= this.CoreOrderRemoved;

                strategyEngine.Dispose();
                base.Dispose();
            }
            _disposed = true;
        }

        private void OnOrderAdded(Order order)
        {
            try
            {
                var orderWrapper = new OrderWrapper(order);
                strategyEngine.ProcessFailSafe(orderWrapper);
            }
            catch (Exception ex)
            {
                LogError($"OnOrderAdded failed for order {order.Id}: {ex}");
                throw;
            }
        }

        private void CoreOrderRemoved(Order order)
        {
            try
            {
                strategyEngine.ReportOrderRemoved(order.Id);
            }
            catch (Exception ex)
            {
                LogError($"OnOrderAdded failed for order {order.Id}: {ex}");
                throw;
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
