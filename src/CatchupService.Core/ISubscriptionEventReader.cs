namespace CatchupService.Core;

public interface ISubscriptionEventReader
{
    IAsyncEnumerable<SubscriptionEvent> ReadAsync(SubscriptionDefinition subscription, CancellationToken cancellationToken = default);
}
