using Microsoft.Extensions.Logging.Abstractions;
using SubscriptionService.Application;
using SubscriptionService.Domain;
using SubscriptionService.Infrastructure;
using SubscriptionService.Worker;

namespace SubscriptionService.Tests;

public sealed class SubscriptionReplayServiceTests
{
    [Fact]
    public async Task ReplayAsync_ContinuesAfterCallerCancellation_AndRemovesParkedEvent()
    {
        var checkpointStore = new InMemoryCheckpointStore();
        var parkedStore = new InMemoryParkedEventStore();
        var replayStore = new InMemoryReplaySessionStore();

        var subscription = CreateSubscription() with { SoftDeleteParked = true };
        var configStore = new InMemorySubscriptionConfigurationStore(new[] { subscription });
        var runtimeRegistry = new WorkerRuntimeRegistry();

        var deliveryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowDeliveryToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var deliveryClient = new ScriptedDeliveryClient(async (@event, cancellationToken) =>
        {
            if (@event.EventId == "parked-1")
            {
                deliveryStarted.TrySetResult();
                await allowDeliveryToFinish.Task.WaitAsync(cancellationToken);
            }

            return DeliveryOutcome.Success;
        });

        var runtime = new SubscriptionRuntime(
            deliveryClient,
            checkpointStore,
            parkedStore,
            replayStore,
            configStore,
            NullLogger<SubscriptionRuntime>.Instance);
        runtimeRegistry.Register(subscription.SubscriptionId, runtime);

        await parkedStore.ParkAsync(ParkedEvent.FromEvent(CreateEvent(commitPosition: 4, eventId: "parked-1"), "needs replay", 1));

        var replayService = new SubscriptionReplayService(
            configStore,
            deliveryClient,
            checkpointStore,
            parkedStore,
            replayStore,
            new InMemorySubscriptionEventSource(),
            new InMemorySubscriptionEventLogStore(),
            NullLoggerFactory.Instance,
            runtimeRegistry,
            WorkerOptions.Default,
            NullLogger<SubscriptionReplayService>.Instance);

        using var cancellationSource = new CancellationTokenSource();

        var result = await replayService.ReplayAsync(subscription.SubscriptionId, cancellationSource.Token);
        Assert.True(result.Started);

        await deliveryStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        cancellationSource.Cancel();
        allowDeliveryToFinish.TrySetResult();

        await WaitUntilAsync(
            async () => !(await parkedStore.GetParkedEventsAsync(subscription.SubscriptionId)).Any(),
            TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ReplayFromCheckpointAsync_DeliversFromCheckpoint_WithoutMovingLiveCheckpoint()
    {
        var checkpointStore = new InMemoryCheckpointStore();
        var parkedStore = new InMemoryParkedEventStore();
        var replayStore = new InMemoryReplaySessionStore();
        var subscription = CreateSubscription();
        var configStore = new InMemorySubscriptionConfigurationStore(new[] { subscription });
        var eventSource = new InMemorySubscriptionEventSource(new[] { CreateEvent(commitPosition: 11) });
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deliveryClient = new ScriptedDeliveryClient((@event, _) =>
        {
            delivered.TrySetResult();
            return Task.FromResult(DeliveryOutcome.Success);
        });
        var runtimeRegistry = new WorkerRuntimeRegistry();

        await checkpointStore.SaveCheckpointAsync(subscription.SubscriptionId, 10, 5, "batch-save", 10);

        var replayService = new SubscriptionReplayService(
            configStore,
            deliveryClient,
            checkpointStore,
            parkedStore,
            replayStore,
            eventSource,
            new InMemorySubscriptionEventLogStore(),
            NullLoggerFactory.Instance,
            runtimeRegistry,
            WorkerOptions.Default,
            NullLogger<SubscriptionReplayService>.Instance);

        var result = await replayService.ReplayFromCheckpointAsync(subscription.SubscriptionId, 10);

        Assert.True(result.Started);
        await delivered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(
            async () => (await replayStore.GetActiveSessionsAsync(subscription.SubscriptionId)).Count == 0,
            TimeSpan.FromSeconds(2));

        var checkpoint = await checkpointStore.GetCheckpointAsync(subscription.SubscriptionId);
        Assert.Equal(10, checkpoint.CommitPosition);
        Assert.Equal(6, checkpoint.ProcessedCount);
    }

    [Fact]
    public async Task ReplayFromCheckpointAsync_DeliversWhenSubscriptionIsDisabledAndRuntimeIsStale()
    {
        var checkpointStore = new InMemoryCheckpointStore();
        var parkedStore = new InMemoryParkedEventStore();
        var replayStore = new InMemoryReplaySessionStore();
        var subscription = CreateSubscription() with { Enabled = false };
        var configStore = new InMemorySubscriptionConfigurationStore(new[] { subscription });
        var eventSource = new InMemorySubscriptionEventSource(new[] { CreateEvent(commitPosition: 11) });
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deliveryClient = new ScriptedDeliveryClient((@event, _) =>
        {
            delivered.TrySetResult();
            return Task.FromResult(DeliveryOutcome.Success);
        });
        var runtimeRegistry = new WorkerRuntimeRegistry();
        var staleRuntime = new SubscriptionRuntime(
            deliveryClient,
            checkpointStore,
            parkedStore,
            replayStore,
            configStore,
            NullLogger<SubscriptionRuntime>.Instance);
        runtimeRegistry.Register(subscription.SubscriptionId, staleRuntime);

        var replayService = new SubscriptionReplayService(
            configStore,
            deliveryClient,
            checkpointStore,
            parkedStore,
            replayStore,
            eventSource,
            new InMemorySubscriptionEventLogStore(),
            NullLoggerFactory.Instance,
            runtimeRegistry,
            WorkerOptions.Default,
            NullLogger<SubscriptionReplayService>.Instance);

        var result = await replayService.ReplayFromCheckpointAsync(subscription.SubscriptionId, 10);

        Assert.True(result.Started);
        await delivered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(
            async () => (await replayStore.GetActiveSessionsAsync(subscription.SubscriptionId)).Count == 0,
            TimeSpan.FromSeconds(2));
    }

    private static SubscriptionDefinition CreateSubscription() =>
        new(
            "sub-1",
            "index-1",
            "https://example.test/subscriptions/sub-1",
            "orders",
            new TimeoutSettings(TimeSpan.FromSeconds(5)),
            new RetrySettings(3, TimeSpan.Zero),
            new CheckpointSettings(1));

    private static SubscriptionEvent CreateEvent(long commitPosition, string eventId = "evt-1") =>
        SubscriptionEvent.Create(
            eventId,
            "sub-1",
            "index-1",
            $"orders-{commitPosition}",
            "order.created",
            new byte[] { 1, 2, 3 },
            "application/json",
            occurredAt: null,
            commitPosition: commitPosition);

    private static async Task WaitUntilAsync(Func<Task<bool>> predicate, TimeSpan timeout)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout)
        {
            if (await predicate())
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("Condition not met within timeout.");
    }

    private sealed class ScriptedDeliveryClient(Func<SubscriptionEvent, CancellationToken, Task<DeliveryOutcome>> handler) : IEventDeliveryClient
    {
        public Task<DeliveryOutcome> DeliverAsync(SubscriptionDefinition subscription, SubscriptionEvent @event, CancellationToken cancellationToken = default) =>
            handler(@event, cancellationToken);
    }
}
