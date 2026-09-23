using Microsoft.Extensions.Logging.Abstractions;
using SubscriptionService.Application;
using SubscriptionService.Domain;
using SubscriptionService.Infrastructure;

namespace SubscriptionService.Tests;

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

        // simple stream event count store that returns 20 for sub-1
        var streamCountStore = new SimpleStreamCountStore(name => name == "sub-1" ? 20L : (long?)null);

        var svc = new SubscriptionStatusService(configStore, checkpointStore, parkedStore, replayStore, runningRegistry, streamCountStore, NullLogger<SubscriptionStatusService>.Instance);

        var status = await svc.GetAsync(subscription.SubscriptionId);

        Assert.NotNull(status);
        Assert.Equal(10, status!.ProcessedCount);
        Assert.Equal(20, status.TotalEventsInSubscription);
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

        var svc = new SubscriptionStatusService(configStore, checkpointStore, parkedStore, replayStore, runningRegistry, streamCountStore, NullLogger<SubscriptionStatusService>.Instance);

        var status = await svc.GetAsync(subscription.SubscriptionId);

        Assert.NotNull(status);
        Assert.Equal(5, status!.ProcessedCount);
        Assert.Equal(0, status.TotalEventsInSubscription);
        Assert.Equal(0d, status.ProgressPercent);

    }

    [Fact]
    public async Task Status_HealthySubscription_ReportsHealthyEvenWhenNotRunning()
    {
        var subscription = new SubscriptionDefinition(
            "sub-2b",
            "index-2b",
            "https://example.test/subscriptions/sub-2b",
            "orders",
            new TimeoutSettings(TimeSpan.FromSeconds(5)),
            new RetrySettings(3, TimeSpan.Zero),
            new CheckpointSettings(1));

        var configStore = new InMemorySubscriptionConfigurationStore(new[] { subscription });
        var checkpointStore = new InMemoryCheckpointStore();
        var parkedStore = new InMemoryParkedEventStore();
        var replayStore = new InMemoryReplaySessionStore();
        var runningRegistry = new NotRunningRegistry();
        var streamCountStore = new SimpleStreamCountStore(_ => 10L);

        await checkpointStore.SaveCheckpointAsync(subscription.SubscriptionId, 4, 4, null);

        var svc = new SubscriptionStatusService(configStore, checkpointStore, parkedStore, replayStore, runningRegistry, streamCountStore, NullLogger<SubscriptionStatusService>.Instance);
        var status = await svc.GetAsync(subscription.SubscriptionId);

        Assert.NotNull(status);
        Assert.Equal("Healthy", status!.Health);
        Assert.False(status.IsRunning);
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

        var svc = new SubscriptionStatusService(configStore, checkpointStore, parkedStore, replayStore, runningRegistry, streamCountStore, NullLogger<SubscriptionStatusService>.Instance);
        var status = await svc.GetAsync(subscription.SubscriptionId);

        Assert.NotNull(status);
        Assert.Equal(150, status!.ProcessedCount);
        Assert.Equal(120, status.TotalEventsInSubscription);
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

        var svc = new SubscriptionStatusService(configStore, checkpointStore, parkedStore, replayStore, runningRegistry, streamCountStore, NullLogger<SubscriptionStatusService>.Instance);
        var status = await svc.GetAsync(subscription.SubscriptionId);

        Assert.NotNull(status);
        Assert.Equal(0, status!.ProcessedCount);
        Assert.Equal(50, status.TotalEventsInSubscription);
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

        var svc = new SubscriptionStatusService(configStore, checkpointStore, parkedStore, replayStore, runningRegistry, streamCountStore, NullLogger<SubscriptionStatusService>.Instance);
        var status = await svc.GetAsync(subscription.SubscriptionId);

        Assert.NotNull(status);
        Assert.Equal(big / 2, status!.ProcessedCount);
        Assert.Equal(big, status.TotalEventsInSubscription);
        Assert.InRange(status.ProgressPercent, 49.9, 50.1);
    }

    [Fact]
    public async Task Status_ShowsFaultedStateAndReason_WhenSubscriptionIsFaulted()
    {
        var subscription = new SubscriptionDefinition(
            "sub-6",
            "index-6",
            "https://example.test/subscriptions/sub-6",
            "orders",
            new TimeoutSettings(TimeSpan.FromSeconds(5)),
            new RetrySettings(3, TimeSpan.Zero),
            new CheckpointSettings(1))
        {
            Enabled = false,
            OperationalState = SubscriptionOperationalState.Faulted,
            OperationalReason = "endpoint unreachable"
        };

        var configStore = new InMemorySubscriptionConfigurationStore(new[] { subscription });
        var checkpointStore = new InMemoryCheckpointStore();
        var parkedStore = new InMemoryParkedEventStore();
        var replayStore = new InMemoryReplaySessionStore();
        var runningRegistry = new WorkerRunningRegistry();
        var streamCountStore = new SimpleStreamCountStore(_ => null);

        var svc = new SubscriptionStatusService(configStore, checkpointStore, parkedStore, replayStore, runningRegistry, streamCountStore, NullLogger<SubscriptionStatusService>.Instance);
        var status = await svc.GetAsync(subscription.SubscriptionId);

        Assert.NotNull(status);
        Assert.Equal("Faulted", status!.Health);
        Assert.Equal(SubscriptionOperationalState.Faulted, status.OperationalState);
        Assert.Equal("endpoint unreachable", status.OperationalReason);
    }

    [Fact]
    public async Task Status_ShowsUnexpectedRuntimeFailure_WhenWorkerStopped()
    {
        var subscription = new SubscriptionDefinition(
            "sub-runtime-failure",
            "index-runtime-failure",
            "https://example.test/subscriptions/sub-runtime-failure",
            "orders",
            new TimeoutSettings(TimeSpan.FromSeconds(5)),
            new RetrySettings(3, TimeSpan.Zero),
            new CheckpointSettings(1));

        var configStore = new InMemorySubscriptionConfigurationStore(new[] { subscription });
        var runningRegistry = new FailureRunningRegistry(new SubscriptionRuntimeFailure(
            "EventStore connection closed",
            new DateTimeOffset(2026, 9, 7, 15, 0, 0, TimeSpan.Zero)));
        var svc = new SubscriptionStatusService(
            configStore,
            new InMemoryCheckpointStore(),
            new InMemoryParkedEventStore(),
            new InMemoryReplaySessionStore(),
            runningRegistry,
            new SimpleStreamCountStore(_ => null),
            NullLogger<SubscriptionStatusService>.Instance);

        var status = await svc.GetAsync(subscription.SubscriptionId);

        Assert.NotNull(status);
        Assert.Equal("EventStore connection closed", status!.RuntimeFailureReason);
        Assert.Equal(new DateTimeOffset(2026, 9, 7, 15, 0, 0, TimeSpan.Zero), status.RuntimeFailureAt);
    }

    private sealed class SimpleStreamCountStore : IStreamEventCountStore
    {
        private readonly Func<string, long?> _f;
        public SimpleStreamCountStore(Func<string, long?> f) => this._f = f;
        public Task<long?> GetTotalForSubscriptionAsync(string subscriptionId, CancellationToken cancellationToken = default) => Task.FromResult(this._f(subscriptionId));
        public Task<StreamEventCountState?> GetStateAsync(string subscriptionId, CancellationToken cancellationToken = default) => Task.FromResult<StreamEventCountState?>(null);
        public Task UpsertAsync(string subscriptionId, string secondaryIndexName, long totalCount, long? lastScannedCommitPosition, DateTimeOffset updatedAt, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    // lightweight running registry used for tests
    private sealed class WorkerRunningRegistry : IRunningSubscriptionRegistry
    {
        public bool IsRunning(string subscriptionId) => true;
        public SubscriptionRuntimeFailure? GetFailure(string subscriptionId) => null;
    }

    private sealed class NotRunningRegistry : IRunningSubscriptionRegistry
    {
        public bool IsRunning(string subscriptionId) => false;
        public SubscriptionRuntimeFailure? GetFailure(string subscriptionId) => null;
    }

    private sealed class FailureRunningRegistry(SubscriptionRuntimeFailure failure) : IRunningSubscriptionRegistry
    {
        public bool IsRunning(string subscriptionId) => false;
        public SubscriptionRuntimeFailure? GetFailure(string subscriptionId) => failure;
    }
}

