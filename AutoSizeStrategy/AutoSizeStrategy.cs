using System;
using TradingPlatform.BusinessLayer;
using TradingPlatform.BusinessLayer.Utils;

namespace AutoSizeStrategy
{
    public class AutoSizeStrategy : Strategy, IStrategyLogger
    {
        // A unique ID to track this specific instance of the strategy
        private string _instanceId;

        private StrategyEngine strategyEngine;

        public AutoSizeStrategy()
        {
            this.Name = "AutoSizeStrategy42";
            this.Description = "Size orders for ALL symbols and accounts according to risk parameters.";
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
            strategyEngine.ProcessRequest(e.RequestParameters);
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
            var orderWrapper = new OrderWrapper(order);  
            strategyEngine.ProcessFailSafe(orderWrapper);
        }

        public void LogError(string message)
        {
            Log(message, StrategyLoggingLevel.Error);
        }
    }
}