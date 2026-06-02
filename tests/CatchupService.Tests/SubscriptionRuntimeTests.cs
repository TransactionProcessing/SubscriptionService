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
        var runtime = new SubscriptionRuntime(
            new ScriptedDeliveryClient(_ => DeliveryOutcome.Success),
            checkpointStore,
            parkedStore,
            replayStore,
            NullLogger<SubscriptionRuntime>.Instance);

        var subscription = CreateSubscription();
        var @event = CreateEvent(sequenceNumber: 1);

        var delivered = await runtime.DeliverLiveAsync(subscription, @event);

        Assert.True(delivered);
        Assert.Equal(1, await checkpointStore.GetCheckpointAsync(subscription.SubscriptionId));
        Assert.Empty(await parkedStore.GetParkedEventsAsync(subscription.SubscriptionId));
    }

    [Fact]
    public async Task DeliverLiveAsync_ParksEventAfterRetryLimit()
    {
        var checkpointStore = new InMemoryCheckpointStore();
        var parkedStore = new InMemoryParkedEventStore();
        var replayStore = new InMemoryReplaySessionStore();
        var runtime = new SubscriptionRuntime(
            new ScriptedDeliveryClient(_ => DeliveryOutcome.Failure("transient failure")),
            checkpointStore,
            parkedStore,
            replayStore,
            NullLogger<SubscriptionRuntime>.Instance);

        var subscription = CreateSubscription(retryAttempts: 2);
        var @event = CreateEvent(sequenceNumber: 9);

        var delivered = await runtime.DeliverLiveAsync(subscription, @event);

        Assert.False(delivered);
        Assert.Equal(0, await checkpointStore.GetCheckpointAsync(subscription.SubscriptionId));

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
            NullLogger<SubscriptionRuntime>.Instance);

        var subscription = CreateSubscription();
        await parkedStore.ParkAsync(ParkedEvent.FromEvent(CreateEvent(sequenceNumber: 4, eventId: "parked-1"), "needs replay", 1));

        var replayTask = runtime.ReplayAsync(subscription);
        await replayStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var liveTask = runtime.DeliverLiveAsync(subscription, CreateEvent(sequenceNumber: 5, eventId: "live-1"));
        await Task.Delay(100);

        Assert.False(liveTask.IsCompleted);
        Assert.Equal(0, await checkpointStore.GetCheckpointAsync(subscription.SubscriptionId));

        allowReplayToFinish.TrySetResult();
        await replayTask;
        await liveTask;

        Assert.Equal(5, await checkpointStore.GetCheckpointAsync(subscription.SubscriptionId));
    }

    private static SubscriptionDefinition CreateSubscription(int retryAttempts = 3) =>
        new(
            "sub-1",
            "index-1",
            "https://example.test/subscriptions/sub-1",
            "orders",
            new TimeoutSettings(TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(10)),
            new RetrySettings(retryAttempts, TimeSpan.Zero),
            new CheckpointSettings(10));

    private static SubscriptionEvent CreateEvent(long sequenceNumber, string eventId = "evt-1") =>
        SubscriptionEvent.Create(
            eventId,
            "sub-1",
            "index-1",
            sequenceNumber,
            $"orders-{sequenceNumber}",
            "order.created",
            [1, 2, 3],
            "application/json");

    private sealed class ScriptedDeliveryClient(Func<SubscriptionEvent, Task<DeliveryOutcome>> handler) : IEventDeliveryClient
    {
        public ScriptedDeliveryClient(Func<SubscriptionEvent, DeliveryOutcome> handler)
            : this(@event => Task.FromResult(handler(@event)))
        {
        }

        public Task<DeliveryOutcome> DeliverAsync(SubscriptionDefinition subscription, SubscriptionEvent @event, CancellationToken cancellationToken = default) =>
            handler(@event);
    }
}
