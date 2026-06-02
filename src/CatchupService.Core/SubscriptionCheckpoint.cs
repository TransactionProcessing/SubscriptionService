namespace CatchupService.Core;

public sealed record SubscriptionCheckpoint(
    string SubscriptionName,
    long Position,
    DateTimeOffset UpdatedAtUtc);
