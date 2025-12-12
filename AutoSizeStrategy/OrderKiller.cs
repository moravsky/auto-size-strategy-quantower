using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using AutoSizeStrategy;
using TradingPlatform.BusinessLayer;

namespace AutoSizeStrategy
{
    public interface IOrderKiller : IDisposable
    {
        void Kill(IOrder order);
        void ReportCancelledOrder(string orderId);
    }

    public class OrderKiller : IOrderKiller
    {
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly ConcurrentDictionary<string, DateTime> _pendingCancels = new();
        private readonly Random _random = new();
        private const int LockCleanupPeriodMs = 5000;
        private const int MaximumCancelAgeMs = 5000;
        private const int MinCancelDelay = 567;
        private const int MaxCancelDelay = 1234;
        private readonly IStrategyLogger _logger;

        public OrderKiller(IStrategyLogger logger)
        {
            _logger = logger;

            // Free locks on the orders that are pending for a while
            _ = Task.Run(async () =>
            {
                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(LockCleanupPeriodMs, _cancellationTokenSource.Token);
                        var now = DateTime.UtcNow;

                        foreach (var kvp in _pendingCancels)
                        {
                            // If a lock is older than 10 seconds, force remove it
                            if ((now - kvp.Value).TotalMilliseconds > MaximumCancelAgeMs)
                            {
                                _pendingCancels.TryRemove(kvp.Key, out _);
                                logger.LogInfo($"Force cleared stuck lock for {kvp.Key}");
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            });
        }

        public void Kill(IOrder order)
        {
            var task = Task.Run(async () =>
            {
                if (order.Status != OrderStatus.Opened)
                    return;

                // atomic lock to avoid multiple threads trying to cancel the same order
                if (!_pendingCancels.TryAdd(order.Id, DateTime.UtcNow))
                {
                    // Already pending cancellation, exit immediately.
                    return;
                }

                // TODO: V2: Add Gaussian noise to the delay?
                // Introduce jitter to avoid overloading Rithmic API
                await Task.Delay(_random.Next(MinCancelDelay, MaxCancelDelay));
                try
                {
                    var tradingOperationResult = order.Cancel();
                    if (tradingOperationResult.Status != TradingOperationResultStatus.Success)
                    {
                        _logger.LogError(
                            $"Order {order.Id} cancelation failed: {tradingOperationResult.Message}"
                        );
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Order {order.Id} cancelation failed: {ex.Message}");
                }
            });
        }

        public void ReportCancelledOrder(string orderId)
        {
            if (!_pendingCancels.TryRemove(orderId, out _))
            {
                _logger.LogInfo($"Order {orderId} already removed");
            }
        }

        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
