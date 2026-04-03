using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TradingPlatform.BusinessLayer;

namespace AutoSizeStrategy
{
    public interface ITradingService : IDisposable
    {
        bool Cancel(string orderId);
        bool CancelReplace(string originalOrderId, IPlaceOrderRequestParameters newParams);

        IEnumerable<IPosition> GetPositions(IAccount account);
        IEnumerable<IOrder> GetWorkingOrders(IAccount account);
        double GetNetPositionQuantity(IAccount account, ISymbol symbol);

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
        IStrategyLogger logger,
        Func<IEnumerable<IPosition>> positionProvider,
        Func<IEnumerable<IOrder>> orderProvider,
        TradingServiceSettings tradingServiceSettings
    ) : ITradingService
    {
        public TradingService(IStrategyLogger logger)
            : this(logger, () => [], () => [], new TradingServiceSettings())
        {
        }

        private readonly CancellationTokenSource _cts = new();
        private readonly TrackingSet<string> _pendingCancels = new();
        private readonly TrackingSet<long> _pendingPlacements = new();
        private readonly Random _random = new();

        public bool Place(IPlaceOrderRequestParameters parameters, bool useLeadingJitter = false)
        {
            if (
                !_pendingPlacements.TryTrack(
                    parameters.RequestId,
                    DateTime.UtcNow.AddMilliseconds(tradingServiceSettings.PlaceExpirationMs)
                )
            )
                return false;

            logger.LogVerbose($"Place {parameters.RequestId}: tracked in pendingPlacements");

            // Fire-and-forget the background retry loop to unblock the caller
            _ = ExecuteWithRetryAsync(
                "PlaceRequest",
                parameters.RequestId.ToString(),
                () => Task.FromResult(parameters.Send()),
                useLeadingJitter: useLeadingJitter,
                shouldContinue: () =>
                    _pendingPlacements.Contains(parameters.RequestId)
                    && !parameters.CancellationToken.IsCancellationRequested
            );

            return true;
        }

        public bool Cancel(string orderId)
        {
            var order = IOrder.Find(orderId);
            if (order == null)
            {
                return false;
            }
            else
            {
                return Cancel(order);
            }
        }

        public bool Cancel(IOrder order, bool useLeadingJitter = false)
        {
            if (
                order.Status != OrderStatus.Opened
                || !_pendingCancels.TryTrack(
                    order.Id,
                    DateTime.UtcNow.AddMilliseconds(tradingServiceSettings.CancelExpirationMs)
                )
            )
                return false;

            _ = ExecuteWithRetryAsync(
                "CancellOrder",
                order.Id,
                () => Task.FromResult(order.Cancel()),
                useLeadingJitter: useLeadingJitter,
                shouldContinue: () =>
                    _pendingCancels.Contains(order.Id) && order.Status != OrderStatus.Cancelled
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
                        logger.LogVerbose($"CancelReplace[{originalOrder.Id}]: starting cancel phase");

                        if (!Cancel(originalOrder, useLeadingJitter: false))
                        {
                            logger.LogVerbose($"CancelReplace[{originalOrder.Id}]: Cancel() returned false, aborting");
                            return;
                        }

                        bool confirmed = await _pendingCancels.WaitAsync(
                            originalOrder.Id,
                            tradingServiceSettings.CancelWaitMs
                        );

                        logger.LogVerbose(
                            $"CancelReplace[{originalOrder.Id}]: cancel confirmed={confirmed} " +
                            $"orderStatus={originalOrder.Status}"
                        );

                        if (
                            newParams.Quantity > MathUtil.Epsilon
                            && (confirmed || originalOrder.Status == OrderStatus.Cancelled)
                        )
                        {
                            logger.LogVerbose(
                                $"CancelReplace[{originalOrder.Id}]: placing replacement " +
                                $"reqId={newParams.RequestId} qty={newParams.Quantity}"
                            );
                            Place(newParams, useLeadingJitter: false);
                        }
                        else
                        {
                            logger.LogError($"CancelReplace: SLA Timeout for {originalOrder.Id}");
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError($"CancelReplace background fail: {ex.Message}");
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
            Func<bool> shouldContinue = null
        )
        {
            int retryDelayMs = tradingServiceSettings.InitialRetryDelayMs;
            for (int i = 0; i <= tradingServiceSettings.MaxRetries; i++)
            {
                if (shouldContinue != null && !shouldContinue())
                {
                    logger.LogVerbose($"{name} {id} aborted pre-jitter (iteration={i})");
                    return TradingOperationResult.CreateSuccess(0, id);
                }

                try
                {
                    if (i > 0 || useLeadingJitter)
                    {
                        await Task.Delay(
                            _random.Next(
                                tradingServiceSettings.MinJitterMs,
                                tradingServiceSettings.MaxJitterMs
                            ),
                            _cts.Token
                        );
                    }

                    // Double-check after the jitter
                    if (shouldContinue != null && !shouldContinue())
                    {
                        logger.LogVerbose($"{name} {id} aborted post-jitter (iteration={i})");
                        return TradingOperationResult.CreateSuccess(0, id);
                    }

                    var res = await op();
                    if (res?.Status == TradingOperationResultStatus.Success)
                        return res;

                    logger.LogVerbose(
                        $"{name} {id} iteration={i} non-success: status={res?.Status} text={res?.Message}"
                    );
                }
                catch (Exception ex)
                {
                    logger.LogError($"{name} {id} exception: {ex.Message}");
                }

                if (i < tradingServiceSettings.MaxRetries)
                {
                    await Task.Delay(retryDelayMs, _cts.Token);
                    retryDelayMs *= 2;
                }
            }

            logger.LogError(
                $"{name} {id} timed out after {tradingServiceSettings.MaxRetries} retries"
            );
            return TradingOperationResult.CreateError(67, $"{name} {id} timed out");
        }

        public IEnumerable<IPosition> GetPositions(IAccount account) =>
            positionProvider().Where(p => p.Account.Id == account.Id);

        public IEnumerable<IOrder> GetWorkingOrders(IAccount account) =>
            orderProvider().Where(o => o.Account.Id == account.Id && o.Status == OrderStatus.Opened);

        public double GetNetPositionQuantity(IAccount account, ISymbol symbol)
        {
            var position = GetPositions(account)
                .FirstOrDefault(p => p.Symbol.Id == symbol.Id);

            if (position == null)
                return 0;

            return position.Side == Side.Buy ? position.Quantity : -position.Quantity;
        }

        public void ReportCancelledOrder(string orderId) => _pendingCancels.TryRemove(orderId);

        public void ReportPlacedOrder(long requestId)
        {
            bool removed = _pendingPlacements.TryRemove(requestId);
            if (removed)
                logger.LogVerbose($"ReportPlacedOrder: removed {requestId} from pendingPlacements");
        }

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