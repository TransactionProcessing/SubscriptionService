using CatchupService.Core;

namespace CatchupService.Infrastructure;

public sealed class NoopSubscriptionEventReader : ISubscriptionEventReader
{
    public async IAsyncEnumerable<SubscriptionEvent> ReadAsync(SubscriptionDefinition subscription, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }
}
