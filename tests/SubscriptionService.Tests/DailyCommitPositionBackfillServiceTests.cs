using Microsoft.Extensions.Logging.Abstractions;
using SubscriptionService.Domain;
using SubscriptionService.Worker;

namespace SubscriptionService.Tests;

public sealed class DailyCommitPositionBackfillServiceTests
{
    [Fact]
    public async Task BackfillAsync_RecordsFirstCommitPositionForEachUtcDay_WithoutChangingScanState()
    {
        var subscription = CreateSubscription();
        var events = new[]
        {
            CreateEvent(subscription, new DateTimeOffset(2026, 9, 1, 23, 0, 0, TimeSpan.Zero), 12),
            CreateEvent(subscription, new DateTimeOffset(2026, 9, 1, 23, 30, 0, TimeSpan.Zero), 13),
            CreateEvent(subscription, new DateTimeOffset(2026, 9, 2, 0, 5, 0, TimeSpan.Zero), 20)
        };
        var eventSource = new FakeHistoricalEventSource(events);
        var dailyStore = new FakeDailyCommitPositionStore();
        var scanStateStore = new FakeScanStateStore(999);
        var service = new DailyCommitPositionBackfillService(
            new FakeSubscriptionConfigurationStore(subscription),
            eventSource,
            dailyStore,
            NullLogger<DailyCommitPositionBackfillService>.Instance);

        var result = await service.BackfillAsync(subscription.SubscriptionId);

        Assert.True(result.Started);
        Assert.Equal(
            new[] { (new DateTime(2026, 9, 1), (long?)12), (new DateTime(2026, 9, 2), (long?)20) },
            dailyStore.Rows.Select(x => (x.Date, x.CommitPosition)));
        Assert.Equal(999, await scanStateStore.GetLastScannedCommitPositionAsync(subscription.SecondaryIndexName));
        Assert.Equal(1, eventSource.ReadFromBeginningCalls);
    }

    private static SubscriptionDefinition CreateSubscription() => new(
        "sub-1",
        "index-1",
        "https://example.test/sub-1",
        "orders",
        TimeoutSettings.Default,
        RetrySettings.Default,
        CheckpointSettings.Default);

    private static SubscriptionEvent CreateEvent(SubscriptionDefinition subscription, DateTimeOffset occurredAt, long commitPosition) =>
        SubscriptionEvent.Create(
            Guid.NewGuid().ToString(),
            subscription.SubscriptionId,
            subscription.SecondaryIndexName,
            $"stream-{commitPosition}",
            "OrderCreated",
            Array.Empty<byte>(),
            "application/json",
            occurredAt: occurredAt,
            commitPosition: commitPosition);

    private sealed class FakeHistoricalEventSource(IReadOnlyCollection<SubscriptionEvent> events) : ISubscriptionEventSource
    {
        public int ReadFromBeginningCalls { get; private set; }

        public Task ReadFromCheckpointAsync(SubscriptionDefinition subscription, CheckpointState checkpoint, Func<SubscriptionEvent, CancellationToken, Task> eventAppeared, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async Task ReadFromBeginningAsync(SubscriptionDefinition subscription, Func<SubscriptionEvent, CancellationToken, Task> eventAppeared, CancellationToken cancellationToken = default)
        {
            this.ReadFromBeginningCalls++;
            foreach (var @event in events)
            {
                await eventAppeared(@event, cancellationToken);
            }
        }

        public Task SubscribeAsync(SubscriptionDefinition subscriptionDefinition, CheckpointState checkpoint, Func<SubscriptionEvent, CancellationToken, Task<bool>> eventAppeared, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<SubscriptionEvent>> PeekAsync(SubscriptionDefinition subscriptionDefinition, int maxCount, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SubscriptionEvent>>(Array.Empty<SubscriptionEvent>());
    }

    private sealed class FakeSubscriptionConfigurationStore(SubscriptionDefinition subscription) : ISubscriptionConfigurationStore
    {
        public Task<IReadOnlyCollection<SubscriptionDefinition>> GetSubscriptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<SubscriptionDefinition>>(new[] { subscription });

        public Task UpsertAsync(SubscriptionDefinition subscription, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetOperationalStateAsync(string subscriptionId, SubscriptionOperationalState operationalState, string? operationalReason = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveAsync(string subscriptionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeDailyCommitPositionStore : IDailyCommitPositionStore
    {
        public List<DailyCommitPositionRecord> Rows { get; } = [];

        public Task UpsertAsync(string subscriptionId, string secondaryIndexName, DateTime date, long? commitPosition, CancellationToken cancellationToken = default)
        {
            this.Rows.RemoveAll(x => x.SubscriptionId == subscriptionId && x.SecondaryIndexName == secondaryIndexName && x.Date == date);
            this.Rows.Add(new DailyCommitPositionRecord(subscriptionId, secondaryIndexName, date, commitPosition));
            return Task.CompletedTask;
        }

        public Task<long?> GetCommitPositionForDateAsync(string subscriptionId, string secondaryIndexName, DateTime date, CancellationToken cancellationToken = default) =>
            Task.FromResult(this.Rows.FirstOrDefault(x => x.SubscriptionId == subscriptionId && x.SecondaryIndexName == secondaryIndexName && x.Date == date)?.CommitPosition);

        public Task<IReadOnlyCollection<DailyCommitPositionRecord>> GetPositionsAsync(string? subscriptionId, string? secondaryIndexName, DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<DailyCommitPositionRecord>>(this.Rows);

        public Task DeleteOlderThanAsync(DateTime threshold, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeScanStateStore(long? position) : IIndexScanStateStore
    {
        public Task<long?> GetLastScannedCommitPositionAsync(string secondaryIndexName, CancellationToken cancellationToken = default) => Task.FromResult(position);
        public Task SetLastScannedCommitPositionAsync(string secondaryIndexName, long? commitPosition, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
