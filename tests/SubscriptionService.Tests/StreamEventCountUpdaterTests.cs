using Microsoft.Extensions.Logging.Abstractions;
using SubscriptionService.Application;
using SubscriptionService.Domain;
using SubscriptionService.Worker;

namespace SubscriptionService.Tests;

public sealed class StreamEventCountUpdaterTests
{
    [Fact]
    public async Task UpdateCountsAsync_ResumesFromCheckpoint_AndPersistsNewTotal()
    {
        var subscription = new SubscriptionDefinition(
            "sub-1",
            "index-1",
            "https://example.test/subscriptions/sub-1",
            "orders",
            new TimeoutSettings(TimeSpan.FromSeconds(5)),
            new RetrySettings(3, TimeSpan.Zero),
            new CheckpointSettings(1));

        var configStore = new FakeSubscriptionConfigurationStore(subscription);
        var countStore = new FakeStreamEventCountStore();
        countStore.Seed("sub-1", new StreamEventCountState(2, 20, DateTimeOffset.UtcNow));

        var reader = new FakeIndexEventReader(
            new IndexEventCountPage(new long[] { 20, 21, 22 }, 23));

        var updater = new StreamEventCountUpdater(reader, configStore, countStore, NullLogger<StreamEventCountUpdater>.Instance);

        await updater.UpdateCountsAsync(CancellationToken.None);

        var state = await countStore.GetStateAsync("sub-1");
        Assert.NotNull(state);
        Assert.Equal(4, state!.TotalCount);
        Assert.Equal(23, state.LastScannedCommitPosition);
        Assert.Single(reader.Calls);
        Assert.Equal("index-1", reader.Calls[0].SecondaryIndexName);
        Assert.Equal(20, reader.Calls[0].AfterCommitPosition);
        Assert.Equal(1000, reader.Calls[0].MaxCount);
    }

    [Fact]
    public async Task UpdateCountsAsync_PersistsCheckpointWhenPageIsEmpty()
    {
        var subscription = new SubscriptionDefinition(
            "sub-2",
            "index-2",
            "https://example.test/subscriptions/sub-2",
            "orders",
            new TimeoutSettings(TimeSpan.FromSeconds(5)),
            new RetrySettings(3, TimeSpan.Zero),
            new CheckpointSettings(1));

        var configStore = new FakeSubscriptionConfigurationStore(subscription);
        var countStore = new FakeStreamEventCountStore();
        var reader = new FakeIndexEventReader(
            new IndexEventCountPage(Array.Empty<long>(), 100));

        var updater = new StreamEventCountUpdater(reader, configStore, countStore, NullLogger<StreamEventCountUpdater>.Instance);

        await updater.UpdateCountsAsync(CancellationToken.None);

        var state = await countStore.GetStateAsync("sub-2");
        Assert.NotNull(state);
        Assert.Equal(0, state!.TotalCount);
        Assert.Equal(100, state.LastScannedCommitPosition);
        Assert.Single(reader.Calls);
        Assert.Null(reader.Calls[0].AfterCommitPosition);
    }

    private sealed class FakeSubscriptionConfigurationStore : ISubscriptionConfigurationStore
    {
        private readonly IReadOnlyCollection<SubscriptionDefinition> _subscriptions;

        public FakeSubscriptionConfigurationStore(params SubscriptionDefinition[] subscriptions)
        {
            this._subscriptions = subscriptions;
        }

        public Task<IReadOnlyCollection<SubscriptionDefinition>> GetSubscriptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(this._subscriptions);

        public Task UpsertAsync(SubscriptionDefinition subscription, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SetOperationalStateAsync(string subscriptionId, SubscriptionOperationalState operationalState, string? operationalReason = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RemoveAsync(string subscriptionId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeIndexEventReader : IIndexEventReader
    {
        private readonly Queue<IndexEventCountPage> _pages;

        public List<(string SecondaryIndexName, long? AfterCommitPosition, int MaxCount)> Calls { get; } = [];

        public FakeIndexEventReader(params IndexEventCountPage[] pages)
        {
            this._pages = new Queue<IndexEventCountPage>(pages);
        }

        public Task<IndexEventCountPage> ReadNextPageAsync(
            string secondaryIndexName,
            long? afterCommitPosition,
            int maxCount,
            CancellationToken cancellationToken = default)
        {
            this.Calls.Add((secondaryIndexName, afterCommitPosition, maxCount));

            return Task.FromResult(
                this._pages.Count > 0
                    ? this._pages.Dequeue()
                    : new IndexEventCountPage(Array.Empty<long>(), afterCommitPosition));
        }
    }

    private sealed class FakeStreamEventCountStore : IStreamEventCountStore
    {
        private readonly Dictionary<string, StreamEventCountState?> _state = new(StringComparer.OrdinalIgnoreCase);

        public void Seed(string subscriptionId, StreamEventCountState? state) => this._state[subscriptionId] = state;

        public Task<long?> GetTotalForSubscriptionAsync(string subscriptionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(this._state.TryGetValue(subscriptionId, out var state) ? state?.TotalCount : null);

        public Task<StreamEventCountState?> GetStateAsync(string subscriptionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(this._state.TryGetValue(subscriptionId, out var state) ? state : null);

        public Task UpsertAsync(
            string subscriptionId,
            string secondaryIndexName,
            long totalCount,
            long? lastScannedCommitPosition,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken = default)
        {
            this._state[subscriptionId] = new StreamEventCountState(totalCount, lastScannedCommitPosition, updatedAt);
            return Task.CompletedTask;
        }
    }
}
