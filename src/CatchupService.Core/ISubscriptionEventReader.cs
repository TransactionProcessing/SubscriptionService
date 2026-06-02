namespace CatchupService.Core;

public interface ISubscriptionEventReader
{
    IAsyncEnumerable<SubscriptionEvent> ReadAsync(
        SubscriptionDefinition subscription,
        SubscriptionCheckpoint? checkpoint,
        CancellationToken cancellationToken = default);
}
