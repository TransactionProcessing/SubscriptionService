namespace SubscriptionService.Worker;

public static class StreamEventCountPersistencePolicy
{
    public const int PersistenceBatchSize = 10_000;

    public static readonly TimeSpan MaximumPersistenceInterval = TimeSpan.FromSeconds(30);

    public static bool ShouldPersist(
        int eventsSincePersistence,
        DateTimeOffset lastPersistedAt,
        DateTimeOffset now) =>
        eventsSincePersistence >= PersistenceBatchSize ||
        now - lastPersistedAt >= MaximumPersistenceInterval;
}
