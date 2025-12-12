using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace AutoSizeStrategy
{
    /// Thread-safe set for tracking items with automatic expiration
    /// Used for idempotency checks and preventing duplicate operations.
    public class TrackingSet<T> : IDisposable
        where T : notnull
    {
        private static readonly TimeSpan DefaultCleanupInterval = TimeSpan.FromMilliseconds(200);
        private static readonly TimeSpan DefaultExpirationTime = TimeSpan.FromSeconds(5);

        private readonly ConcurrentDictionary<T, DateTime> _tracked = new();
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
            return _tracked.TryAdd(key, expirationDate);
        }

        public bool TryTrack(T key)
        {
            return TryTrack(key, DateTime.UtcNow + _defaultExpirationTime);
        }

        public bool Contains(T key)
        {
            return _tracked.ContainsKey(key);
        }

        public bool TryRemove(T key, out DateTime value)
        {
            return _tracked.TryRemove(key, out value);
        }

        public bool TryRemove(T key)
        {
            DateTime _;
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
                        if (kvp.Value < DateTime.UtcNow)
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
