using SubscriptionService.Application;
using SubscriptionService.Domain;

namespace SubscriptionService.Tests.Integration;

internal sealed class SimpleInMemorySubscriptionConfigurationStore : ISubscriptionConfigurationStore
{
    private readonly Func<CancellationToken, Task<IReadOnlyCollection<SubscriptionDefinition>>> _getter;
    public SimpleInMemorySubscriptionConfigurationStore(Func<CancellationToken, Task<IReadOnlyCollection<SubscriptionDefinition>>> getter) => this._getter = getter;
    public Task<IReadOnlyCollection<SubscriptionDefinition>> GetSubscriptionsAsync(CancellationToken cancellationToken = default) => this._getter(cancellationToken);
    public Task UpsertAsync(SubscriptionDefinition subscription, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SetOperationalStateAsync(string subscriptionId, SubscriptionOperationalState operationalState, string? operationalReason = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RemoveAsync(string subscriptionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class NoopEventSource : ISubscriptionEventSource
{
    public Task ReadFromCheckpointAsync(SubscriptionDefinition subscriptionDefinition, CheckpointState checkpoint, Func<SubscriptionEvent, CancellationToken, Task> eventAppeared, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task ReadFromBeginningAsync(SubscriptionDefinition subscriptionDefinition, Func<SubscriptionEvent, CancellationToken, Task> eventAppeared, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SubscribeAsync(SubscriptionDefinition subscriptionDefinition, CheckpointState checkpoint, Func<SubscriptionEvent, CancellationToken, Task<bool>> eventAppeared, CancellationToken cancellationToken = default)
        => Task.Delay(Timeout.Infinite, cancellationToken);

    public Task<IReadOnlyList<SubscriptionEvent>> PeekAsync(SubscriptionDefinition subscriptionDefinition, int maxCount, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<SubscriptionEvent>>(Array.Empty<SubscriptionEvent>());
}

internal sealed class NoopCheckpointStore : ICheckpointStore
{
    public Task<CheckpointState> GetCheckpointAsync(string subscriptionId, CancellationToken cancellationToken = default) => Task.FromResult(new CheckpointState(null, 0L, null));
    public Task SaveCheckpointAsync(string subscriptionId, long? commitPosition, long processedCount, string? checkpointReason = null, long? preparePosition = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RemoveAsync(string subscriptionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class NoopParkedEventStore : IParkedEventStore
{
    public Task ParkAsync(ParkedEvent parkedEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<IReadOnlyCollection<ParkedEvent>> GetParkedEventsAsync(string subscriptionId, CancellationToken cancellationToken = default) => Task.FromResult((IReadOnlyCollection<ParkedEvent>)Array.Empty<ParkedEvent>());
    public Task RemoveParkedEventAsync(string subscriptionId, Guid parkedEventId, ISubscriptionConfigurationStore configurationStore, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class NoopReplaySessionStore : IReplaySessionStore
{
    public Task<ReplaySession> StartAsync(string subscriptionId, CancellationToken cancellationToken = default) => Task.FromResult(new ReplaySession(Guid.NewGuid(), subscriptionId, DateTimeOffset.UtcNow, null));
    public Task CompleteAsync(Guid replaySessionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<IReadOnlyCollection<ReplaySession>> GetActiveSessionsAsync(string subscriptionId, CancellationToken cancellationToken = default) => Task.FromResult((IReadOnlyCollection<ReplaySession>)Array.Empty<ReplaySession>());
}

internal sealed class NoopDeliveryClient : IEventDeliveryClient
{
    public Task<DeliveryOutcome> DeliverAsync(SubscriptionDefinition subscription, SubscriptionEvent @event, CancellationToken cancellationToken = default) => Task.FromResult(DeliveryOutcome.Success);
}

internal sealed class NoopSubscriptionEventLogStore : ISubscriptionEventLogStore
{
    public Task AddAsync(SubscriptionEventLog eventLog, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
