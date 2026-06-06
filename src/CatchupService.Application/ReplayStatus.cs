namespace CatchupService.Application;

public sealed record ReplayStatus(
    string SubscriptionId,
    bool HasActiveReplaySession,
    int ActiveReplaySessionCount);

public sealed record ReplayOperationResult(
    string SubscriptionId,
    bool Started,
    string Message,
    bool WasQueued,
    bool UsedLiveRuntime);