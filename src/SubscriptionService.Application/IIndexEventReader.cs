namespace SubscriptionService.Application;

public interface IIndexEventReader
{
    Task<IndexEventCountPage> ReadNextPageAsync(
        string secondaryIndexName,
        long? afterCommitPosition,
        int maxCount,
        CancellationToken cancellationToken = default);
}
