using System;
using System.Threading.Tasks;
using Xunit;

namespace AutoSizeStrategy.Tests
{
    public class TrackingSetTests
    {
        [Fact]
        public void TryTrack_NewItem_ReturnsTrue()
        {
            using var set = new TrackingSet<string>();

            var result = set.TryTrack("item1");

            Assert.True(result);
            Assert.Equal(1, set.Count);
        }

        [Fact]
        public void TryTrack_DuplicateItem_ReturnsFalse()
        {
            using var set = new TrackingSet<string>();

            set.TryTrack("item1");
            var result = set.TryTrack("item1");

            Assert.False(result);
            Assert.Equal(1, set.Count);
        }

        [Fact]
        public void TryTrack_WithExpirationDate_TracksItem()
        {
            using var set = new TrackingSet<int>();
            var expiration = DateTime.UtcNow.AddMinutes(5);

            var result = set.TryTrack(42, expiration);

            Assert.True(result);
            Assert.True(set.Contains(42));
        }

        [Fact]
        public void Contains_TrackedItem_ReturnsTrue()
        {
            using var set = new TrackingSet<string>();
            set.TryTrack("exists");

            Assert.True(set.Contains("exists"));
        }

        [Fact]
        public void Contains_UntrackedItem_ReturnsFalse()
        {
            using var set = new TrackingSet<string>();

            Assert.False(set.Contains("missing"));
        }

        [Fact]
        public void TryRemove_ExistingItem_ReturnsTrueAndRemoves()
        {
            using var set = new TrackingSet<string>();
            set.TryTrack("item");

            var result = set.TryRemove("item");

            Assert.True(result);
            Assert.False(set.Contains("item"));
            Assert.Equal(0, set.Count);
        }

        [Fact]
        public void TryRemove_NonExistingItem_ReturnsFalse()
        {
            using var set = new TrackingSet<string>();

            var result = set.TryRemove("missing");

            Assert.False(result);
        }

        [Fact]
        public void TryRemove_WithOutParam_ReturnsExpirationDate()
        {
            using var set = new TrackingSet<string>();
            var expirationTime = DateTime.UtcNow.AddMinutes(10);
            set.TryTrack("item", expirationTime);

            var result = set.TryRemove("item", out var returnedExpiration);

            Assert.True(result);
            Assert.Equal(expirationTime, returnedExpiration.Time);
        }

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
        public void Count_ReflectsTrackedItems()
        {
            using var set = new TrackingSet<int>();

            Assert.Equal(0, set.Count);

            set.TryTrack(1);
            set.TryTrack(2);
            set.TryTrack(3);

            Assert.Equal(3, set.Count);

            set.TryRemove(2);

            Assert.Equal(2, set.Count);
        }

        [Fact]
        public void Dispose_CanBeCalledMultipleTimes()
        {
            var set = new TrackingSet<string>();

            set.Dispose();
            set.Dispose(); // Should not throw
        }

        [Fact]
        public void TrackingSet_WorksWithValueTypes()
        {
            using var set = new TrackingSet<int>();

            Assert.True(set.TryTrack(42));
            Assert.True(set.Contains(42));
            Assert.False(set.TryTrack(42));
        }

        [Fact]
        public void TrackingSet_WorksWithCustomTypes()
        {
            using var set = new TrackingSet<Guid>();
            var id = Guid.NewGuid();

            Assert.True(set.TryTrack(id));
            Assert.True(set.Contains(id));
            Assert.False(set.Contains(Guid.NewGuid()));
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

        [Fact]
        public void Dispose_ShouldNotThrow()
        {
            var set = new TrackingSet<string>();
            var exception = Record.Exception(() => set.Dispose());
            Assert.Null(exception);
        }
    }
}
