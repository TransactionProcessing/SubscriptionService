using CatchupService.Application;
using CatchupService.Domain;
using CatchupService.Infrastructure;

namespace CatchupService.Tests;

public sealed class SubscriptionStatusServiceTests
{
    [Fact]
    public async Task Status_IncludesProgress_WhenStreamTotalAvailable()
    {
        var subscription = new SubscriptionDefinition(
            "sub-1",
            "index-1",
            "https://example.test/subscriptions/sub-1",
            "orders",
            new TimeoutSettings(TimeSpan.FromSeconds(5)),
            new RetrySettings(3, TimeSpan.Zero),
            new CheckpointSettings(1));

        var configStore = new InMemorySubscriptionConfigurationStore(new[] { subscription });
        var checkpointStore = new InMemoryCheckpointStore();
        var parkedStore = new InMemoryParkedEventStore();
        var replayStore = new InMemoryReplaySessionStore();
        var runningRegistry = new WorkerRunningRegistry();

        // seed checkpoint processed count
        await checkpointStore.SaveCheckpointAsync(subscription.SubscriptionId, 10, 10, null);

        // simple stream event count store that returns 20 for index-1
        var streamCountStore = new SimpleStreamCountStore(name => name == "index-1" ? 20L : (long?)null);

        var svc = new SubscriptionStatusService(configStore, checkpointStore, parkedStore, replayStore, runningRegistry, streamCountStore);

        var status = await svc.GetAsync(subscription.SubscriptionId);

        Assert.NotNull(status);
        Assert.Equal(10, status!.ProcessedCount);
        Assert.Equal(20, status.TotalEventsInIndex);
        Assert.InRange(status.ProgressPercent, 49.9, 50.1);
    }

    [Fact]
    public async Task Status_ProgressIsNullWhenNoTotal()
    {
        var subscription = new SubscriptionDefinition(
            "sub-2",
            "index-missing",
            "https://example.test/subscriptions/sub-2",
            "orders",
            new TimeoutSettings(TimeSpan.FromSeconds(5)),
            new RetrySettings(3, TimeSpan.Zero),
            new CheckpointSettings(1));

        var configStore = new InMemorySubscriptionConfigurationStore(new[] { subscription });
        var checkpointStore = new InMemoryCheckpointStore();
        var parkedStore = new InMemoryParkedEventStore();
        var replayStore = new InMemoryReplaySessionStore();
        var runningRegistry = new WorkerRunningRegistry();

        await checkpointStore.SaveCheckpointAsync(subscription.SubscriptionId, 5, 5, null);

        var streamCountStore = new SimpleStreamCountStore(_ => null);

        var svc = new SubscriptionStatusService(configStore, checkpointStore, parkedStore, replayStore, runningRegistry, streamCountStore);

        var status = await svc.GetAsync(subscription.SubscriptionId);

        Assert.NotNull(status);
        Assert.Equal(5, status!.ProcessedCount);
        Assert.Equal(0, status.TotalEventsInIndex);
        Assert.Equal(0d, status.ProgressPercent);

    }

    [Fact]
    public async Task Status_ProcessedGreaterThanTotal_CappedAt100Percent()
    {
        var subscription = new SubscriptionDefinition(
            "sub-3",
            "index-3",
            "https://example.test/subscriptions/sub-3",
            "orders",
            new TimeoutSettings(TimeSpan.FromSeconds(5)),
            new RetrySettings(3, TimeSpan.Zero),
            new CheckpointSettings(1));

        var configStore = new InMemorySubscriptionConfigurationStore(new[] { subscription });
        var checkpointStore = new InMemoryCheckpointStore();
        var parkedStore = new InMemoryParkedEventStore();
        var replayStore = new InMemoryReplaySessionStore();
        var runningRegistry = new WorkerRunningRegistry();

        await checkpointStore.SaveCheckpointAsync(subscription.SubscriptionId, 100, 150, null);

        var streamCountStore = new SimpleStreamCountStore(_ => 120L);

        var svc = new SubscriptionStatusService(configStore, checkpointStore, parkedStore, replayStore, runningRegistry, streamCountStore);
        var status = await svc.GetAsync(subscription.SubscriptionId);

        Assert.NotNull(status);
        Assert.Equal(150, status!.ProcessedCount);
        Assert.Equal(120, status.TotalEventsInIndex);
        Assert.Equal(100d, status.ProgressPercent);
        Assert.Equal("100% (150/120)", status.ProgressDisplay);
    }

    [Fact]
    public async Task Status_ZeroProcessed_ShowsZeroPercent()
    {
        var subscription = new SubscriptionDefinition(
            "sub-4",
            "index-4",
            "https://example.test/subscriptions/sub-4",
            "orders",
            new TimeoutSettings(TimeSpan.FromSeconds(5)),
            new RetrySettings(3, TimeSpan.Zero),
            new CheckpointSettings(1));

        var configStore = new InMemorySubscriptionConfigurationStore(new[] { subscription });
        var checkpointStore = new InMemoryCheckpointStore();
        var parkedStore = new InMemoryParkedEventStore();
        var replayStore = new InMemoryReplaySessionStore();
        var runningRegistry = new WorkerRunningRegistry();

        await checkpointStore.SaveCheckpointAsync(subscription.SubscriptionId, 0, 0, null);

        var streamCountStore = new SimpleStreamCountStore(_ => 50L);

        var svc = new SubscriptionStatusService(configStore, checkpointStore, parkedStore, replayStore, runningRegistry, streamCountStore);
        var status = await svc.GetAsync(subscription.SubscriptionId);

        Assert.NotNull(status);
        Assert.Equal(0, status!.ProcessedCount);
        Assert.Equal(50, status.TotalEventsInIndex);
        Assert.Equal(0d, status.ProgressPercent);
    }

    [Fact]
    public async Task Status_VeryLargeNumbers_DoesNotOverflow()
    {
        var subscription = new SubscriptionDefinition(
            "sub-5",
            "index-5",
            "https://example.test/subscriptions/sub-5",
            "orders",
            new TimeoutSettings(TimeSpan.FromSeconds(5)),
            new RetrySettings(3, TimeSpan.Zero),
            new CheckpointSettings(1));

        var configStore = new InMemorySubscriptionConfigurationStore(new[] { subscription });
        var checkpointStore = new InMemoryCheckpointStore();
        var parkedStore = new InMemoryParkedEventStore();
        var replayStore = new InMemoryReplaySessionStore();
        var runningRegistry = new WorkerRunningRegistry();

        var big = (long)int.MaxValue * 4L;
        await checkpointStore.SaveCheckpointAsync(subscription.SubscriptionId, big, big / 2, null);

        var streamCountStore = new SimpleStreamCountStore(_ => big);

        var svc = new SubscriptionStatusService(configStore, checkpointStore, parkedStore, replayStore, runningRegistry, streamCountStore);
        var status = await svc.GetAsync(subscription.SubscriptionId);

        Assert.NotNull(status);
        Assert.Equal(big / 2, status!.ProcessedCount);
        Assert.Equal(big, status.TotalEventsInIndex);
        Assert.InRange(status.ProgressPercent, 49.9, 50.1);
    }

    private sealed class SimpleStreamCountStore : IStreamEventCountStore
    {
        private readonly Func<string, long?> _f;
        public SimpleStreamCountStore(Func<string, long?> f) => _f = f;
        public Task<long?> GetTotalForIndexAsync(string secondaryIndexName, CancellationToken cancellationToken = default) => Task.FromResult(_f(secondaryIndexName));
    }

    // lightweight running registry used for tests
    private sealed class WorkerRunningRegistry : IRunningSubscriptionRegistry { public bool IsRunning(string subscriptionId) => true; }
}
