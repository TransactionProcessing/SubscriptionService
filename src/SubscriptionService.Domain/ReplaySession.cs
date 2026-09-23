namespace SubscriptionService.Domain;

public sealed record ReplaySession(
    Guid ReplaySessionId,
    string SubscriptionId,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt);
