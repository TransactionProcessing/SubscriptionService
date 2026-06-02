namespace CatchupService.Core;

public interface ICheckpointStore
{
    Task<SubscriptionCheckpoint?> GetAsync(string subscriptionName, CancellationToken cancellationToken = default);

    Task SaveAsync(SubscriptionCheckpoint checkpoint, CancellationToken cancellationToken = default);
}
