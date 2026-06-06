namespace CatchupService.Application;

public interface ISubscriptionReplayService
{
    Task<ReplayOperationResult> ReplayAsync(string subscriptionId, CancellationToken cancellationToken = default);

    Task<ReplayStatus?> GetStatusAsync(string subscriptionId, CancellationToken cancellationToken = default);
}