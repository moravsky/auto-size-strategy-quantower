using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace AutoSizeStrategy
{
    public record Expiration(DateTime Time, TaskCompletionSource<bool> Completion) { }

    /// Thread-safe set for tracking items with automatic expiration
    /// Used for idempotency checks and preventing duplicate operations.
    public class TrackingSet<T> : IDisposable
        where T : notnull
    {
        private static readonly TimeSpan DefaultCleanupInterval = TimeSpan.FromMilliseconds(200);
        private static readonly TimeSpan DefaultExpirationTime = TimeSpan.FromSeconds(5);

        private readonly ConcurrentDictionary<T, Expiration> _tracked = new();
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly TimeSpan _defaultExpirationTime;
        private bool _disposed;

        public TrackingSet(TimeSpan? cleanupInterval = null, TimeSpan? defaultExpirationTime = null)
        {
            _defaultExpirationTime = defaultExpirationTime ?? DefaultExpirationTime;
            _ = RunCleanupLoop(cleanupInterval ?? DefaultCleanupInterval);
        }

        public bool TryTrack(T key, DateTime expirationDate)
        {
            var tcs = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            return _tracked.TryAdd(key, new Expiration(expirationDate, tcs));
        }

        public bool TryTrack(T key)
        {
            return TryTrack(key, DateTime.UtcNow + _defaultExpirationTime);
        }

        // Allows a caller to await the removal/cancellation of a specific tracked item.
        public Task<bool> WaitAsync(T key, int timeoutMs = 5000)
        {
            if (!_tracked.TryGetValue(key, out var expiration))
                return Task.FromResult(true); // Already gone

            var timeoutCts = new CancellationTokenSource(timeoutMs);
            timeoutCts.Token.Register(() => expiration.Completion.TrySetResult(false)); // False on timeout

            return expiration.Completion.Task;
        }

        public bool Contains(T key)
        {
            return _tracked.ContainsKey(key);
        }

        public bool TryRemove(T key, out Expiration entry)
        {
            if (_tracked.TryRemove(key, out entry))
            {
                entry.Completion.TrySetResult(true); // Signal success to anyone waiting
                return true;
            }
            return false;
        }

        public bool TryRemove(T key)
        {
            Expiration _;
            return TryRemove(key, out _);
        }

        /// <summary>
        /// Current number of tracked items.
        /// </summary>
        public int Count => _tracked.Count;

        public void Dispose()
        {
            if (!_disposed)
            {
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource.Dispose();
                _disposed = true;
                GC.SuppressFinalize(this);
            }
        }

        private async Task RunCleanupLoop(TimeSpan interval)
        {
            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(interval, _cancellationTokenSource.Token);

                    foreach (var kvp in _tracked)
                    {
                        if (kvp.Value.Time < DateTime.UtcNow)
                        {
                            TryRemove(kvp.Key);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
