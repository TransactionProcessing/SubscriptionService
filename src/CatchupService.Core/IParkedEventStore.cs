using CatchupService.Contracts;

namespace CatchupService.Core;

public interface IParkedEventStore
{
    Task StoreAsync(ParkedSubscriptionEvent parkedEvent, CancellationToken cancellationToken = default);
}
