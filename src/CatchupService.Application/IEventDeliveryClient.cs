using CatchupService.Domain;

namespace CatchupService.Application;

public interface IEventDeliveryClient
{
    Task<DeliveryOutcome> DeliverAsync(
        SubscriptionDefinition subscription,
        SubscriptionEvent @event,
        CancellationToken cancellationToken = default);
}
