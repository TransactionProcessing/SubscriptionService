namespace SubscriptionService.Application;

public interface IRunningSubscriptionRegistry
{
    bool IsRunning(string subscriptionId);
    SubscriptionRuntimeFailure? GetFailure(string subscriptionId);
    SubscriptionRuntimeState? GetState(string subscriptionId) => null;
}

public sealed record SubscriptionRuntimeFailure(string Message, DateTimeOffset OccurredAt);

public sealed record SubscriptionRuntimeState(
    DateTimeOffset? LastStartedAt,
    DateTimeOffset? LastStoppedAt,
    string? LastStopReason,
    int StopCount);
