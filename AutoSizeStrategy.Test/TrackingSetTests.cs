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
            var expiration = DateTime.UtcNow.AddMinutes(10);
            set.TryTrack("item", expiration);

            var result = set.TryRemove("item", out var returnedExpiration);

            Assert.True(result);
            Assert.Equal(expiration, returnedExpiration);
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
    }
}
