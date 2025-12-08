using System;
using TradingPlatform.BusinessLayer;
using TradingPlatform.BusinessLayer.Utils;

namespace AutoSizeStrategy
{
    public interface IStrategyLogger
    {
        void LogError(string message);
        void LogInfo(string message);
    }

    public class AutoSizeStrategy : Strategy, IStrategyLogger
    {
        // A unique ID to track this specific instance of the strategy
        private string _instanceId;

        private StrategyEngine strategyEngine;

        [InputParameter("Risk Percent", minimum: 1.0, maximum: 100.0, increment: 1.0)]
        public double RiskPercent { get; set; } = 10.0;

        public AutoSizeStrategy()
        {
            this.Name = "AutoSizeStrategy42";
            this.Description =
                "Size orders for ALL symbols and accounts according to risk parameters.";
            this.strategyEngine = new StrategyEngine(this);
        }

        // Called ONCE when you add the strategy to the list
        protected override void OnCreated()
        {
            // Generate a short unique ID (e.g., "A1B2")
            _instanceId = Guid.NewGuid().ToString().Substring(0, 4).ToUpper();
        }

        // Called when you click "Start"
        protected override void OnRun()
        {
            Core.OrderAdded += OnOrderAdded;
            Core.NewRequest += this.CoreNewRequest;
        }

        private void CoreNewRequest(object sender, RequestEventArgs e)
        {
            try
            {
                // Call the engine (static) – any error goes to the engine’s own logs
                strategyEngine.ProcessRequest(e.RequestParameters);
            }
            catch (Exception ex)
            {
                // The strategy receives the failure and writes it to the system log
                LogError($"CoreNewRequest failed: {ex}");
                // Re-throw the exception so that the host can handle it (e.g., show a popup)
                throw;
            }
        }

        // Called when you click "Stop"
        protected override void OnStop()
        {
            UnsubscribeEvents();
        }

        // Called ONCE when you click "X" to remove the strategy
        protected override void OnRemove()
        {
            // It's safe to double unsubscribe
            UnsubscribeEvents();
        }

        protected void UnsubscribeEvents()
        {
            Core.OrderAdded -= OnOrderAdded;
            Core.NewRequest -= this.CoreNewRequest;
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
                // Re-throw the exception so that the host can handle it (e.g., show a popup)
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
