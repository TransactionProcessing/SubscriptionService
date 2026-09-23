using SubscriptionService.Worker;

namespace SubscriptionService.Tests;

public sealed class StreamEventCountPersistencePolicyTests
{
    [Fact]
    public void ShouldPersist_WhenEventsArriveAndTimeThresholdIsReached()
    {
        var lastPersistedAt = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

        var shouldPersist = StreamEventCountPersistencePolicy.ShouldPersist(
            eventsSincePersistence: 1,
            lastPersistedAt,
            lastPersistedAt.AddSeconds(30));

        Assert.True(shouldPersist);
    }

    [Fact]
    public void ShouldPersist_WhenBatchThresholdIsReachedBeforeTimeThreshold()
    {
        var lastPersistedAt = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

        var shouldPersist = StreamEventCountPersistencePolicy.ShouldPersist(
            StreamEventCountPersistencePolicy.PersistenceBatchSize,
            lastPersistedAt,
            lastPersistedAt.AddSeconds(1));

        Assert.True(shouldPersist);
    }

    [Fact]
    public void ShouldNotPersist_WhenNeitherThresholdIsReached()
    {
        var lastPersistedAt = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

        var shouldPersist = StreamEventCountPersistencePolicy.ShouldPersist(
            eventsSincePersistence: 1,
            lastPersistedAt,
            lastPersistedAt.AddSeconds(29));

        Assert.False(shouldPersist);
    }
}
