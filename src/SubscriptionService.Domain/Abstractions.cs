namespace SubscriptionService.Domain;

public interface IEndpointStore
{
    Task<IReadOnlyCollection<EndpointDefinition>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<EndpointDefinition?> GetAsync(int endpointId, CancellationToken cancellationToken = default);
    Task UpsertAsync(EndpointDefinition endpoint, CancellationToken cancellationToken = default);
    Task<bool> RemoveAsync(int endpointId, CancellationToken cancellationToken = default);
}

public interface ISubscriptionConfigurationStore
{
    Task<IReadOnlyCollection<SubscriptionDefinition>> GetSubscriptionsAsync(CancellationToken cancellationToken = default);

    Task UpsertAsync(SubscriptionDefinition subscription, CancellationToken cancellationToken = default);

    Task SetOperationalStateAsync(
        string subscriptionId,
        SubscriptionOperationalState operationalState,
        string? operationalReason = null,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(string subscriptionId, CancellationToken cancellationToken = default);
}

public interface ICheckpointStore
{
    Task<CheckpointState> GetCheckpointAsync(string subscriptionId, CancellationToken cancellationToken = default);

    Task SaveCheckpointAsync(
        string subscriptionId,
        long? commitPosition,
        long processedCount,
        string? checkpointReason = null,
        long? preparePosition = null,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(string subscriptionId, CancellationToken cancellationToken = default);
}

public sealed record CheckpointState(long? CommitPosition, long ProcessedCount, string? CheckpointReason, long? PreparePosition = null);

public interface IParkedEventStore
{
    Task ParkAsync(ParkedEvent parkedEvent, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<ParkedEvent>> GetParkedEventsAsync(string subscriptionId, CancellationToken cancellationToken = default);

    Task RemoveParkedEventAsync(string subscriptionId, Guid parkedEventId, ISubscriptionConfigurationStore configurationStore, CancellationToken cancellationToken = default);
}

public interface IReplaySessionStore
{
    Task<ReplaySession> StartAsync(string subscriptionId, CancellationToken cancellationToken = default);

    Task CompleteAsync(Guid replaySessionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<ReplaySession>> GetActiveSessionsAsync(string subscriptionId, CancellationToken cancellationToken = default);
}

public interface ISubscriptionEventSource
{
    Task ReadFromCheckpointAsync(
        SubscriptionDefinition subscriptionDefinition,
        CheckpointState checkpoint,
        Func<SubscriptionEvent, CancellationToken, Task> eventAppeared,
        CancellationToken cancellationToken = default);

    Task ReadFromBeginningAsync(
        SubscriptionDefinition subscriptionDefinition,
        Func<SubscriptionEvent, CancellationToken, Task> eventAppeared,
        CancellationToken cancellationToken = default);

    Task SubscribeAsync(
        SubscriptionDefinition subscriptionDefinition,
        CheckpointState checkpoint,
        Func<SubscriptionEvent, CancellationToken, Task<bool>> eventAppeared,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionEvent>> PeekAsync(
        SubscriptionDefinition subscriptionDefinition,
        int maxCount,
        CancellationToken cancellationToken = default);
}

public interface IDailyCommitPositionStore
{
    Task UpsertAsync(string subscriptionId, string secondaryIndexName, DateTime date, long? commitPosition, CancellationToken cancellationToken = default);

    Task<long?> GetCommitPositionForDateAsync(string subscriptionId, string secondaryIndexName, DateTime date, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<DailyCommitPositionRecord>> GetPositionsAsync(string? subscriptionId, string? secondaryIndexName, DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);

    Task DeleteOlderThanAsync(DateTime threshold, CancellationToken cancellationToken = default);
}

public sealed record DailyCommitPositionRecord(string SubscriptionId, string SecondaryIndexName, DateTime Date, long? CommitPosition);

public interface IIndexScanStateStore
{
    Task<long?> GetLastScannedCommitPositionAsync(string secondaryIndexName, CancellationToken cancellationToken = default);

    Task SetLastScannedCommitPositionAsync(string secondaryIndexName, long? commitPosition, CancellationToken cancellationToken = default);
}

public sealed record SubscriptionEventLog(
    string EventId,
    string SubscriptionId,
    string Payload,
    string AddressSentTo,
    int? ResponseStatusCode,
    string EventType,
    string? ResponseBody,
    DateTimeOffset ProcessedAt,
    int DeliveryAttempt,
    bool IsReplay);

public interface ISubscriptionEventLogStore
{
    Task AddAsync(SubscriptionEventLog eventLog, CancellationToken cancellationToken = default);
}

public sealed record BuiltInIndexCatalogEntry(string Name, string Type, long? LastSeenCommitPosition, DateTimeOffset UpdatedAt);

public interface IBuiltInIndexCatalogStore
{
    Task<IReadOnlyList<BuiltInIndexCatalogEntry>> GetAllAsync(CancellationToken cancellationToken = default);

    Task UpsertAsync(string name, string type, long? lastSeenCommitPosition, CancellationToken cancellationToken = default);
}
