namespace CatchupService.Contracts;

public sealed record SubscriptionReplayRequest(
    string SubscriptionName,
    long FromPosition,
    long? ToPosition = null);
