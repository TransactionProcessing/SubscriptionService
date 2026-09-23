namespace SubscriptionService.Worker;

public sealed record CountConnectionSnapshot(
    string SubscriptionId,
    string SecondaryIndexName,
    long TotalCount,
    long? LastCommitPosition,
    bool IsCaughtUp,
    string State,
    string? LastEventId,
    string? LastStreamName,
    string? LastEventType,
    DateTimeOffset UpdatedAt,
    string? FailureReason);
