using CatchupService.Application;
using CatchupService.Domain;
using CatchupService.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace CatchupService.Tests;

public sealed class SubscriptionRuntimeTests
{
    [Fact]
    public async Task DeliverLiveAsync_AdvancesCheckpointAfterSuccess()
    {
        var checkpointStore = new InMemoryCheckpointStore();
        var parkedStore = new InMemoryParkedEventStore();
        var replayStore = new InMemoryReplaySessionStore();
        var configStore = new InMemorySubscriptionConfigurationStore(new[] { CreateSubscription() });
        var runtime = new SubscriptionRuntime(
            new ScriptedDeliveryClient(_ => DeliveryOutcome.Success),
            checkpointStore,
            parkedStore,
            replayStore,
            configStore,
            NullLogger<SubscriptionRuntime>.Instance);

        var subscription = CreateSubscription();
        var @event = CreateEvent(commitPosition: 1);

        var delivered = await runtime.DeliverLiveAsync(subscription, @event);

        Assert.True(delivered);
        var cp1 = await checkpointStore.GetCheckpointAsync(subscription.SubscriptionId);
        Assert.Equal(1, cp1.CommitPosition);
        Assert.Empty(await parkedStore.GetParkedEventsAsync(subscription.SubscriptionId));
    }

    [Fact]
    public async Task Replay_RemovesParkedEvent_SoftDeleteEnabled()
    {
        var checkpointStore = new InMemoryCheckpointStore();
        var parkedStore = new InMemoryParkedEventStore();
        var replayStore = new InMemoryReplaySessionStore();

        var subscription = CreateSubscription();
        subscription = subscription with { SoftDeleteParked = true };

        var configStore = new InMemorySubscriptionConfigurationStore(new[] { subscription });

        var runtime = new SubscriptionRuntime(
            new ScriptedDeliveryClient(_ => DeliveryOutcome.Success),
            checkpointStore,
            parkedStore,
            replayStore,
            configStore,
            NullLogger<SubscriptionRuntime>.Instance);

        await parkedStore.ParkAsync(ParkedEvent.FromEvent(CreateEvent(commitPosition: 4, eventId: "parked-1"), "needs replay", 1));

        await runtime.ReplayAsync(subscription);

        var remaining = await parkedStore.GetParkedEventsAsync(subscription.SubscriptionId);
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task Replay_RemovesParkedEvent_PhysicalDelete()
    {
        var checkpointStore = new InMemoryCheckpointStore();
        var parkedStore = new InMemoryParkedEventStore();
        var replayStore = new InMemoryReplaySessionStore();

        var subscription = CreateSubscription();
        subscription = subscription with { SoftDeleteParked = false };

        var configStore = new InMemorySubscriptionConfigurationStore(new[] { subscription });

        var runtime = new SubscriptionRuntime(
            new ScriptedDeliveryClient(_ => DeliveryOutcome.Success),
            checkpointStore,
            parkedStore,
            replayStore,
            configStore,
            NullLogger<SubscriptionRuntime>.Instance);

        await parkedStore.ParkAsync(ParkedEvent.FromEvent(CreateEvent(commitPosition: 4, eventId: "parked-1"), "needs replay", 1));

        await runtime.ReplayAsync(subscription);

        var remaining = await parkedStore.GetParkedEventsAsync(subscription.SubscriptionId);
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task DeliverLiveAsync_ParksEventAfterRetryLimit()
    {
        var checkpointStore = new InMemoryCheckpointStore();
        var parkedStore = new InMemoryParkedEventStore();
        var replayStore = new InMemoryReplaySessionStore();
        var configStore2 = new InMemorySubscriptionConfigurationStore(new[] { CreateSubscription(retryAttempts: 2) });
        var runtime = new SubscriptionRuntime(
            new ScriptedDeliveryClient(_ => DeliveryOutcome.Failure("transient failure")),
            checkpointStore,
            parkedStore,
            replayStore,
            configStore2,
            NullLogger<SubscriptionRuntime>.Instance);

        var subscription = CreateSubscription(retryAttempts: 2);
        var @event = CreateEvent(commitPosition: 9);

        var delivered = await runtime.DeliverLiveAsync(subscription, @event);

        Assert.False(delivered);
        var cp2 = await checkpointStore.GetCheckpointAsync(subscription.SubscriptionId);
        Assert.Equal(0, cp2.CommitPosition);

        var parkedEvents = await parkedStore.GetParkedEventsAsync(subscription.SubscriptionId);
        var parkedEvent = Assert.Single(parkedEvents);
        Assert.Equal("transient failure", parkedEvent.FailureReason);
        Assert.Equal(2, parkedEvent.AttemptCount);
        Assert.Equal("orders-9", parkedEvent.StreamName);
    }

    [Fact]
    public async Task ReplayAsync_PausesLiveProcessing_AndDoesNotChangeCheckpoint()
    {
        var checkpointStore = new InMemoryCheckpointStore();
        var parkedStore = new InMemoryParkedEventStore();
        var replayStore = new InMemoryReplaySessionStore();
        var replayStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowReplayToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var configStore3 = new InMemorySubscriptionConfigurationStore(new[] { CreateSubscription() });
        var runtime = new SubscriptionRuntime(
            new ScriptedDeliveryClient(async @event =>
            {
                if (@event.EventId == "parked-1")
                {
                    replayStarted.TrySetResult();
                    await allowReplayToFinish.Task;
                }

                return DeliveryOutcome.Success;
            }),
            checkpointStore,
            parkedStore,
            replayStore,
            configStore3,
            NullLogger<SubscriptionRuntime>.Instance);

        var subscription = CreateSubscription();
        await parkedStore.ParkAsync(ParkedEvent.FromEvent(CreateEvent(commitPosition: 4, eventId: "parked-1"), "needs replay", 1));

        var replayTask = runtime.ReplayAsync(subscription);
        await replayStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var liveTask = runtime.DeliverLiveAsync(subscription, CreateEvent(commitPosition: 5, eventId: "live-1"));
        await Task.Delay(100);

        Assert.False(liveTask.IsCompleted);
        var cp3 = await checkpointStore.GetCheckpointAsync(subscription.SubscriptionId);
        Assert.Equal(0, cp3.CommitPosition);

        allowReplayToFinish.TrySetResult();
        await replayTask;
        await liveTask;

        var cp4 = await checkpointStore.GetCheckpointAsync(subscription.SubscriptionId);
        Assert.Equal(5, cp4.CommitPosition);
    }

    private static SubscriptionDefinition CreateSubscription(int retryAttempts = 3) =>
        new(
            "sub-1",
            "index-1",
            "https://example.test/subscriptions/sub-1",
            "orders",
            new TimeoutSettings(TimeSpan.FromSeconds(5)),
            new RetrySettings(retryAttempts, TimeSpan.Zero),
            new CheckpointSettings(1));

    private static SubscriptionDefinition CreateDisabledSubscription() =>
        new(
            "sub-2",
            "index-1",
            "https://example.test/subscriptions/sub-2",
            "orders",
            new TimeoutSettings(TimeSpan.FromSeconds(5)),
            new RetrySettings(3, TimeSpan.Zero),
            new CheckpointSettings(10))
        { Enabled = false };

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

    private sealed class ScriptedDeliveryClient(Func<SubscriptionEvent, Task<DeliveryOutcome>> handler) : IEventDeliveryClient
    {
        public ScriptedDeliveryClient(Func<SubscriptionEvent, DeliveryOutcome> handler)
            : this(@event => Task.FromResult(handler(@event)))
        {
        }

        public Task<DeliveryOutcome> DeliverAsync(SubscriptionDefinition subscription, SubscriptionEvent @event, CancellationToken cancellationToken = default) =>
            handler(@event);
    }

    [Fact]
    public async Task DeliverLiveAsync_IgnoresDisabledSubscription()
    {
        var checkpointStore = new InMemoryCheckpointStore();
        var parkedStore = new InMemoryParkedEventStore();
        var replayStore = new InMemoryReplaySessionStore();
        var configStore4 = new InMemorySubscriptionConfigurationStore(new[] { CreateSubscription() });
        var runtime = new SubscriptionRuntime(
            new ScriptedDeliveryClient(_ => DeliveryOutcome.Success),
            checkpointStore,
            parkedStore,
            replayStore,
            configStore4,
            NullLogger<SubscriptionRuntime>.Instance);

        var subscription = CreateSubscription();
        subscription = subscription with { Enabled = false };

        var @event = CreateEvent(commitPosition: 1);

        var delivered = await runtime.DeliverLiveAsync(subscription, @event);

        Assert.False(delivered);
        var cp = await checkpointStore.GetCheckpointAsync(subscription.SubscriptionId);
        Assert.Equal(0, cp.CommitPosition);
        Assert.Empty(await parkedStore.GetParkedEventsAsync(subscription.SubscriptionId));
    }
}
