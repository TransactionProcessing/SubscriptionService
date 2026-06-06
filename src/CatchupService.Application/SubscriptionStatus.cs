namespace CatchupService.Application;

public sealed record SubscriptionStatus(
    string SubscriptionId,
    string SecondaryIndexName,
    string EndpointUrl,
    string Tag,
    bool IsRunning,
    string Health,
    long? CheckpointSequenceNumber,
    long? CommitPosition,
    string? CheckpointReason,
    int ParkedEventCount,
    DateTimeOffset? LatestParkedAt,
    string? LatestParkedFailureReason,
    bool HasActiveReplaySession,
    long ProcessedCount,
    long TotalEventsInIndex,
    double ProgressPercent,
    string? ProgressDisplay);

public interface ISubscriptionStatusService
{
    Task<IReadOnlyCollection<SubscriptionStatus>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<SubscriptionStatus?> GetAsync(string subscriptionId, CancellationToken cancellationToken = default);
}