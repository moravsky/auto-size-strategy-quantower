using System;
using System.Threading;
using System.Threading.Tasks;
using TradingPlatform.BusinessLayer;

namespace AutoSizeStrategy
{
    public interface ITradingService : IDisposable
    {
        // TODO: Replace bool with rich result object
        bool Cancel(IOrder order, bool useLeadingJitter = false);
        bool Place(IPlaceOrderRequestParameters parameters, bool useLeadingJitter = false);
        bool CancelReplace(string originalOrderId, IPlaceOrderRequestParameters newParams);
        bool CancelReplace(IOrder originalOrder, IPlaceOrderRequestParameters newParams);

        // Hooks for platform events
        void ReportCancelledOrder(string orderId);
        void ReportPlacedOrder(long requestId);
    }

    public record TradingServiceSettings(
        int MinJitterMs = TradingServiceSettings.DefaultMinJitterMs,
        int MaxJitterMs = TradingServiceSettings.DefaultMaxJitterMs,
        int MaxRetries = TradingServiceSettings.DefaultMaxRetries,
        int InitialRetryDelayMs = TradingServiceSettings.DefaultInitialRetryDelayMs,
        int PlaceExpirationMs = TradingServiceSettings.DefaultPlaceExpirationMs,
        int CancelExpirationMs = TradingServiceSettings.DefaultCancelExpirationMs,
        int CancelWaitMs = TradingServiceSettings.DefaultCancelWaitMs
    )
    {
        // Constants for jitter/retry/expiration
        private const int DefaultMinJitterMs = 567;
        private const int DefaultMaxJitterMs = 1234;
        private const int DefaultMaxRetries = 3;
        private const int DefaultInitialRetryDelayMs = 400;
        private const int DefaultPlaceExpirationMs = 20_000;
        private const int DefaultCancelExpirationMs = 20_000;
        private const int DefaultCancelWaitMs = 5_000;
    }

    public class TradingService(
        IStrategyLogger _logger,
        TradingServiceSettings _tradingServiceSettings = default
    ) : ITradingService
    {
        public TradingService(IStrategyLogger logger)
            : this(logger, new TradingServiceSettings()) { }

        private readonly CancellationTokenSource _cts = new();
        private readonly TrackingSet<string> _pendingCancels = new();
        private readonly TrackingSet<long> _pendingPlacements = new();
        private readonly Random _random = new();

        public bool Place(IPlaceOrderRequestParameters parameters, bool useLeadingJitter = false)
        {
            if (
                !_pendingPlacements.TryTrack(
                    parameters.RequestId,
                    DateTime.UtcNow.AddMilliseconds(_tradingServiceSettings.PlaceExpirationMs)
                )
            )
                return false;

            var abortTask = _pendingPlacements.GetTask(parameters.RequestId);

            // Fire-and-forget the background retry loop to unblock the caller
            _ = ExecuteWithRetryAsync(
                "PlaceRequest",
                parameters.RequestId.ToString(),
                () => Task.FromResult(parameters.Send()),
                useLeadingJitter: useLeadingJitter,
                opDone: _pendingPlacements.GetTask(parameters.RequestId)
            );

            return true;
        }

        public bool Cancel(IOrder order, bool useLeadingJitter = false)
        {
            if (
                order.Status != OrderStatus.Opened
                || !_pendingCancels.TryTrack(
                    order.Id,
                    DateTime.UtcNow.AddMilliseconds(_tradingServiceSettings.CancelExpirationMs)
                )
            )
                return false;

            var abortTask = _pendingCancels.GetTask(order.Id);
            _ = ExecuteWithRetryAsync(
                "CancellOrder",
                order.Id,
                () => Task.FromResult(order.Cancel()),
                useLeadingJitter: useLeadingJitter,
                opDone: _pendingCancels.GetTask(order.Id)
            );
            return true;
        }

        public bool CancelReplace(string orderId, IPlaceOrderRequestParameters newParams)
        {
            var order = IOrder.Find(orderId);
            if (order == null)
            {
                return false;
            }
            return CancelReplace(order, newParams);
        }

        public bool CancelReplace(IOrder originalOrder, IPlaceOrderRequestParameters newParams)
        {
            // Start background orchestration without blocking the StrategyEngine
            _ = Task.Run(
                async () =>
                {
                    try
                    {
                        if (!Cancel(originalOrder, useLeadingJitter: false))
                            return;

                        bool confirmed = await _pendingCancels.WaitAsync(
                            originalOrder.Id,
                            _tradingServiceSettings.CancelWaitMs
                        );
                        if (confirmed || originalOrder.Status == OrderStatus.Cancelled)
                        {
                            Place(newParams, useLeadingJitter: false);
                        }
                        else
                        {
                            _logger.LogError($"CancelReplace: SLA Timeout for {originalOrder.Id}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"CancelReplace background fail: {ex.Message}");
                    }
                },
                _cts.Token
            );

            return true;
        }

        private async Task<TradingOperationResult> ExecuteWithRetryAsync(
            string name,
            string id,
            Func<Task<TradingOperationResult>> op,
            bool useLeadingJitter = false,
            Task<bool> opDone = null
        )
        {
            int retryDelayMs = _tradingServiceSettings.InitialRetryDelayMs;
            for (int i = 0; i <= _tradingServiceSettings.MaxRetries; i++)
            {
                if (opDone != null && opDone.IsCompleted)
                {
                    _logger.LogInfo($"{name} {id} aborted - operation finalized elsewhere");
                    return TradingOperationResult.CreateSuccess(0, id);
                }

                try
                {
                    if (i > 0 || useLeadingJitter)
                    {
                        await Task.Delay(
                            _random.Next(
                                _tradingServiceSettings.MinJitterMs,
                                _tradingServiceSettings.MaxJitterMs
                            ),
                            _cts.Token
                        );
                    }

                    // Double-check after the jitter
                    if (opDone != null && opDone.IsCompleted)
                    {
                        _logger.LogInfo($"{name} {id} aborted - operation finalized elsewhere");
                        return TradingOperationResult.CreateSuccess(0, id);
                    }

                    var res = await op();
                    if (res?.Status == TradingOperationResultStatus.Success)
                        return res;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"{name} {id} exception: {ex.Message}");
                }

                if (i < _tradingServiceSettings.MaxRetries)
                {
                    await Task.Delay(retryDelayMs, _cts.Token);
                    retryDelayMs *= 2;
                }
            }
            _logger.LogError(
                $"{name} {id} timed out after {_tradingServiceSettings.MaxRetries} retries"
            );
            return TradingOperationResult.CreateError(67, $"{name} {id} timed out");
        }

        public void ReportCancelledOrder(string orderId) => _pendingCancels.TryRemove(orderId);

        public void ReportPlacedOrder(long requestId) => _pendingPlacements.TryRemove(requestId);

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
            _pendingCancels.Dispose();
            _pendingPlacements.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
