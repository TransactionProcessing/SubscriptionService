namespace SubscriptionService.Application;

public sealed record IndexEventCountPage(IReadOnlyCollection<long> CommitPositions, long? ResumeCommitPosition);
