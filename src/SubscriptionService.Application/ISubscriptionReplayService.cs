namespace SubscriptionService.Application;

public interface ISubscriptionReplayService
{
    Task<ReplayOperationResult> ReplayAsync(string subscriptionId, CancellationToken cancellationToken = default);

    Task<ReplayOperationResult> ReplayFromCheckpointAsync(string subscriptionId, long commitPosition, CancellationToken cancellationToken = default);

    Task<ReplayStatus?> GetStatusAsync(string subscriptionId, CancellationToken cancellationToken = default);
}
