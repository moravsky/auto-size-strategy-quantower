using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using TradingPlatform.BusinessLayer;

namespace AutoSizeStrategy
{
    public interface IOrderKiller : IDisposable
    {
        void Kill(IOrder order);
        void ReportCancelledOrder(string orderId);
    }

    public class OrderKiller(IStrategyLogger _logger) : IOrderKiller
    {
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly TrackingSet<String> _pendingCancels = new();
        private readonly Random _random = new();
        private const int MinCancelDelayMs = 567;
        private const int MaxCancelDelayMs = 1234;

        public void Kill(IOrder order)
        {
            if (order.Status != OrderStatus.Opened)
                return;

            // atomic lock to avoid multiple threads trying to cancel the same order
            if (!_pendingCancels.TryTrack(order.Id))
            {
                // Already pending cancellation, exit immediately.
                return;
            }

            _ = Task.Run(
                async () =>
                {
                    try
                    {
                        // TODO: V2: Add Gaussian noise to the delay?
                        // Introduce jitter to avoid overloading Rithmic API
                        await Task.Delay(
                            _random.Next(MinCancelDelayMs, MaxCancelDelayMs),
                            _cancellationTokenSource.Token
                        );

                        var tradingOperationResult = order.Cancel();
                        if (tradingOperationResult.Status != TradingOperationResultStatus.Success)
                        {
                            _logger.LogError(
                                $"Order {order.Id} cancelation failed: {tradingOperationResult.Message}"
                            );
                        }
                    }
                    catch (OperationCanceledException ex)
                    {
                        _logger.LogInfo($"Order {order.Id} killer shutting down: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Order {order.Id} cancelation failed: {ex.Message}");
                    }
                },
                _cancellationTokenSource.Token
            );
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
