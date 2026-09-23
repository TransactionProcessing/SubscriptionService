using SubscriptionService.Domain;

namespace SubscriptionService.Application;

public sealed record SubscriptionStatus(
    string SubscriptionId,
    string SecondaryIndexName,
    string EndpointUrl,
    string Tag,
    bool IsRunning,
    string Health,
    SubscriptionOperationalState OperationalState,
    string? OperationalReason,
    long? CheckpointSequenceNumber,
    long? CommitPosition,
    string? CheckpointReason,
    int ParkedEventCount,
    DateTimeOffset? LatestParkedAt,
    string? LatestParkedFailureReason,
    bool HasActiveReplaySession,
    long ProcessedCount,
    long TotalEventsInSubscription,
    double ProgressPercent,
    string? ProgressDisplay,
    string? RuntimeFailureReason,
    DateTimeOffset? RuntimeFailureAt,
    DateTimeOffset? LastStartedAt = null,
    DateTimeOffset? LastStoppedAt = null,
    string? LastStopReason = null,
    int StopCount = 0);

public interface ISubscriptionStatusService
{
    Task<IReadOnlyCollection<SubscriptionStatus>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<SubscriptionStatus?> GetAsync(string subscriptionId, CancellationToken cancellationToken = default);
}
