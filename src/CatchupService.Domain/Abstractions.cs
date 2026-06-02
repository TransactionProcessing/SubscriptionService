namespace CatchupService.Domain;

public interface ISubscriptionConfigurationStore
{
    Task<IReadOnlyCollection<SubscriptionDefinition>> GetSubscriptionsAsync(CancellationToken cancellationToken = default);
}

public interface ICheckpointStore
{
    Task<long> GetCheckpointAsync(string subscriptionId, CancellationToken cancellationToken = default);

    Task SaveCheckpointAsync(string subscriptionId, long checkpoint, CancellationToken cancellationToken = default);
}

public interface IParkedEventStore
{
    Task ParkAsync(ParkedEvent parkedEvent, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<ParkedEvent>> GetParkedEventsAsync(string subscriptionId, CancellationToken cancellationToken = default);
}

public interface IReplaySessionStore
{
    Task<ReplaySession> StartAsync(string subscriptionId, CancellationToken cancellationToken = default);

    Task CompleteAsync(Guid replaySessionId, CancellationToken cancellationToken = default);
}

public interface ISubscriptionEventSource
{
    Task<IReadOnlyCollection<SubscriptionEvent>> ReadBatchAsync(
        string secondaryIndexName,
        long afterSequenceNumber,
        int take,
        CancellationToken cancellationToken = default);
}
