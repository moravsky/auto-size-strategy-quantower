namespace AutoSizeStrategy.Test
{
    public class TrackingSetTests
    {
        [Fact]
        public async Task CleanupLoop_RemovesExpiredItems()
        {
            using var set = new TrackingSet<string>(
                cleanupInterval: TimeSpan.FromMilliseconds(50),
                defaultExpirationTime: TimeSpan.FromMilliseconds(100)
            );

            set.TryTrack("shortLived");
            Assert.True(set.Contains("shortLived"));

            // Wait for expiration + cleanup cycle
            await Task.Delay(200);

            Assert.False(set.Contains("shortLived"));
            Assert.Equal(0, set.Count);
        }

        [Fact]
        public async Task CleanupLoop_KeepsUnexpiredItems()
        {
            using var set = new TrackingSet<string>(
                cleanupInterval: TimeSpan.FromMilliseconds(50),
                defaultExpirationTime: TimeSpan.FromSeconds(10)
            );

            set.TryTrack("longLived");

            await Task.Delay(150);

            Assert.True(set.Contains("longLived"));
        }

        [Fact]
        public async Task CleanupLoop_MixedExpiration_OnlyRemovesExpired()
        {
            using var set = new TrackingSet<string>(cleanupInterval: TimeSpan.FromMilliseconds(50));

            set.TryTrack("expired", DateTime.UtcNow.AddMilliseconds(50));
            set.TryTrack("valid", DateTime.UtcNow.AddSeconds(10));

            await Task.Delay(150);

            Assert.False(set.Contains("expired"));
            Assert.True(set.Contains("valid"));
            Assert.Equal(1, set.Count);
        }

        [Fact]
        public async Task IdempotencyScenario_PreventsDuplicateProcessing()
        {
            using var set = new TrackingSet<string>(defaultExpirationTime: TimeSpan.FromSeconds(5));

            var orderId = "ORDER-123";
            var processedCount = 0;

            // Simulate multiple attempts to process the same order
            for (int i = 0; i < 5; i++)
            {
                if (set.TryTrack(orderId))
                {
                    processedCount++;
                    // Simulate processing
                    await Task.Delay(10);
                }
            }

            Assert.Equal(1, processedCount);
        }

        [Fact]
        public async Task WaitAsync_WhenItemIsRemoved_ReturnsTrue()
        {
            using var set = new TrackingSet<string>();
            set.TryTrack("process-1");

            // Start waiting in the background
            var waitTask = set.WaitAsync("process-1");

            // Simulate the process finishing and removing the item
            set.TryRemove("process-1");

            var result = await waitTask;
            Assert.True(result);
        }

        [Fact]
        public async Task WaitAsync_WhenItemDoesNotExist_ReturnsTrueImmediately()
        {
            using var set = new TrackingSet<string>();

            // If the key isn't there, it should be considered "done"
            var result = await set.WaitAsync("ghost-item");

            Assert.True(result);
        }

        [Fact]
        public async Task WaitAsync_OnTimeout_ReturnsFalse()
        {
            using var set = new TrackingSet<string>();
            set.TryTrack("slow-process", DateTime.MaxValue);

            // Set a very short timeout for the test
            var result = await set.WaitAsync("slow-process", timeoutMs: 10);

            Assert.False(result);
        }

        [Fact]
        public async Task CleanupLoop_WhenExpiring_TriggersWaitAsync()
        {
            // Set short intervals for the test
            using var set = new TrackingSet<string>(
                cleanupInterval: TimeSpan.FromMilliseconds(50),
                defaultExpirationTime: TimeSpan.FromMilliseconds(100)
            );

            set.TryTrack("expiring-item1");
            var waitTask1 = set.WaitAsync("expiring-item1");
            set.TryTrack("expiring-item2");
            var waitTask2 = set.WaitAsync("expiring-item2");

            // Wait for cleanup loop to run
            await Task.Delay(250);

            // The cleanup loop calls TryRemove, which should trigger the TCS
            Assert.True(waitTask1.IsCompleted);
            Assert.True(await waitTask1);
            Assert.False(set.Contains("expiring-item1"));
            Assert.True(waitTask2.IsCompleted);
            Assert.True(await waitTask2);
            Assert.False(set.Contains("expiring-item2"));
        }

        [Fact]
        public async Task WaitAsync_MultipleCallers_IndependentTimeouts()
        {
            using var set = new TrackingSet<string>();
            set.TryTrack("multi-wait");

            var shortWait = set.WaitAsync("multi-wait", timeoutMs: 50); // Should time out
            var longWait = set.WaitAsync("multi-wait", timeoutMs: 5000); // Should succeed

            await Task.Delay(100);
            set.TryRemove("multi-wait"); // Trigger success

            Assert.False(await shortWait); // Hit its 50ms SLA
            Assert.True(await longWait); // Eventually got the 'true' signal
        }
    }
}
