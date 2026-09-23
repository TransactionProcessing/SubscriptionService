using Microsoft.Extensions.Logging.Abstractions;
using SubscriptionService.Application;
using SubscriptionService.Domain;
using SubscriptionService.Infrastructure;

namespace SubscriptionService.Tests;

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

        Assert.True(delivered.IsSuccess);
        var cp1 = await checkpointStore.GetCheckpointAsync(subscription.SubscriptionId);
        Assert.Equal(1, cp1.CommitPosition);
        Assert.Empty(await parkedStore.GetParkedEventsAsync(subscription.SubscriptionId));

        var snapshot = runtime.GetLastDeliverySnapshot();
        Assert.NotNull(snapshot);
        Assert.Equal(@event.EventId, snapshot!.EventId);
        Assert.Equal(@event.StreamName, snapshot.StreamName);
        Assert.Equal("Delivered", snapshot.Status);
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
    public async Task Replay_ReplacesParkedEvent_WithOneFreshRow_WhenReplayFails()
    {
        var checkpointStore = new InMemoryCheckpointStore();
        var parkedStore = new InMemoryParkedEventStore();
        var replayStore = new InMemoryReplaySessionStore();

        var subscription = CreateSubscription();
        subscription = subscription with { SoftDeleteParked = true };

        var configStore = new InMemorySubscriptionConfigurationStore(new[] { subscription });

        var runtime = new SubscriptionRuntime(
            new ScriptedDeliveryClient(_ => DeliveryOutcome.Failure("endpoint unreachable")),
            checkpointStore,
            parkedStore,
            replayStore,
            configStore,
            NullLogger<SubscriptionRuntime>.Instance);

        var originalEvent = ParkedEvent.FromEvent(CreateEvent(commitPosition: 4, eventId: "parked-1"), "needs replay", 1);
        await parkedStore.ParkAsync(originalEvent);

        await runtime.ReplayAsync(subscription);

        var remaining = await parkedStore.GetParkedEventsAsync(subscription.SubscriptionId);
        var parkedEvent = Assert.Single(remaining);
        Assert.Equal(originalEvent.EventId, parkedEvent.EventId);
        Assert.NotEqual(originalEvent.ParkedEventId, parkedEvent.ParkedEventId);
        Assert.Equal("endpoint unreachable", parkedEvent.FailureReason);
        Assert.Equal(3, parkedEvent.AttemptCount);

        var snapshot = runtime.GetLastDeliverySnapshot();
        Assert.NotNull(snapshot);
        Assert.Equal(originalEvent.EventId, snapshot!.EventId);
        Assert.Equal("Failed", snapshot.Status);
        Assert.Equal("endpoint unreachable", snapshot.FailureReason);
    }

    [Fact]
    public async Task ReplayAsync_AdvancesProcessedCount_WithoutAdvancingCommitPosition()
    {
        var checkpointStore = new InMemoryCheckpointStore();
        var parkedStore = new InMemoryParkedEventStore();
        var replayStore = new InMemoryReplaySessionStore();

        var subscription = CreateSubscription();
        var configStore = new InMemorySubscriptionConfigurationStore(new[] { subscription });

        await checkpointStore.SaveCheckpointAsync(subscription.SubscriptionId, 42, 10, "batch-save");
        await parkedStore.ParkAsync(ParkedEvent.FromEvent(CreateEvent(commitPosition: 4, eventId: "parked-1"), "needs replay", 1));

        var runtime = new SubscriptionRuntime(
            new ScriptedDeliveryClient(_ => DeliveryOutcome.Success),
            checkpointStore,
            parkedStore,
            replayStore,
            configStore,
            NullLogger<SubscriptionRuntime>.Instance);

        await runtime.ReplayAsync(subscription);

        var cp = await checkpointStore.GetCheckpointAsync(subscription.SubscriptionId);
        Assert.Equal(42, cp.CommitPosition);
        Assert.Equal(11, cp.ProcessedCount);
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

        Assert.False(delivered.IsSuccess);
        Assert.Equal("transient failure", delivered.FailureReason);
        var cp2 = await checkpointStore.GetCheckpointAsync(subscription.SubscriptionId);
        Assert.Equal(0, cp2.CommitPosition);

        var parkedEvents = await parkedStore.GetParkedEventsAsync(subscription.SubscriptionId);
        var parkedEvent = Assert.Single(parkedEvents);
        Assert.Equal("transient failure", parkedEvent.FailureReason);
        Assert.Equal(2, parkedEvent.AttemptCount);
        Assert.Equal("orders-9", parkedEvent.StreamName);
    }

    [Fact]
    public async Task DeliverLiveAsync_LogsEveryDeliveryAttemptWithAttemptDetails()
    {
        var checkpointStore = new InMemoryCheckpointStore();
        var parkedStore = new InMemoryParkedEventStore();
        var replayStore = new InMemoryReplaySessionStore();
        var logStore = new InMemorySubscriptionEventLogStore();
        var subscription = CreateSubscription(retryAttempts: 2) with { EnableEventLogging = true };
        var runtime = new SubscriptionRuntime(
            new ScriptedDeliveryClient(_ => DeliveryOutcome.Failure("failed", 503, "unavailable")),
            checkpointStore, parkedStore, replayStore,
            new InMemorySubscriptionConfigurationStore(new[] { subscription }),
            NullLogger<SubscriptionRuntime>.Instance,
            logStore);

        await runtime.DeliverLiveAsync(subscription, CreateEvent(9));

        var logs = logStore.Entries;
        Assert.Equal(2, logs.Count);
        Assert.Equal(1, logs[0].DeliveryAttempt);
        Assert.Equal(2, logs[1].DeliveryAttempt);
        Assert.All(logs, log =>
        {
            Assert.False(log.IsReplay);
            Assert.Equal("evt-1", log.EventId);
            Assert.Equal("sub-1", log.SubscriptionId);
            Assert.Equal("\u0001\u0002\u0003", log.Payload);
            Assert.Equal("https://example.test/subscriptions/sub-1", log.AddressSentTo);
            Assert.Equal(503, log.ResponseStatusCode);
            Assert.Equal("unavailable", log.ResponseBody);
            Assert.Equal("order.created", log.EventType);
        });
    }

    [Fact]
    public async Task ReplayAsync_LogsAttemptAsReplay()
    {
        var checkpointStore = new InMemoryCheckpointStore();
        var parkedStore = new InMemoryParkedEventStore();
        var replayStore = new InMemoryReplaySessionStore();
        var logStore = new InMemorySubscriptionEventLogStore();
        var subscription = CreateSubscription() with { EnableEventLogging = true };
        var configStore = new InMemorySubscriptionConfigurationStore(new[] { subscription });
        var runtime = new SubscriptionRuntime(
            new ScriptedDeliveryClient(_ => DeliveryOutcome.Success),
            checkpointStore, parkedStore, replayStore, configStore,
            NullLogger<SubscriptionRuntime>.Instance, logStore);

        await parkedStore.ParkAsync(ParkedEvent.FromEvent(CreateEvent(4, "parked-1"), "failed", 1));
        await runtime.ReplayAsync(subscription);

        var log = Assert.Single(logStore.Entries);
        Assert.True(log.IsReplay);
        Assert.Equal(1, log.DeliveryAttempt);
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

        Assert.False(delivered.IsSuccess);
        var cp = await checkpointStore.GetCheckpointAsync(subscription.SubscriptionId);
        Assert.Equal(0, cp.CommitPosition);
        Assert.Empty(await parkedStore.GetParkedEventsAsync(subscription.SubscriptionId));
    }
}
