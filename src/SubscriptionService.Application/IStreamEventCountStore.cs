namespace SubscriptionService.Application;

public sealed record StreamEventCountState(long TotalCount, long? LastScannedCommitPosition, DateTimeOffset UpdatedAt);

public interface IStreamEventCountStore
{
    /// <summary>
    /// Get the total number of events recorded for a subscription, or null if unknown.
    /// </summary>
    Task<long?> GetTotalForSubscriptionAsync(string subscriptionId, CancellationToken cancellationToken = default);

    Task<StreamEventCountState?> GetStateAsync(string subscriptionId, CancellationToken cancellationToken = default);

    Task UpsertAsync(
        string subscriptionId,
        string secondaryIndexName,
        long totalCount,
        long? lastScannedCommitPosition,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);
}
