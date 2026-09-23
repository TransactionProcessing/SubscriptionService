namespace SubscriptionService.Worker;

public sealed record CountWorkerStatusSnapshot(
    int ConfiguredSubscriptions,
    int AttemptedSubscriptions,
    int FailedSubscriptions,
    DateTimeOffset? StartedAt,
    DateTimeOffset UpdatedAt,
    string? LastError);
