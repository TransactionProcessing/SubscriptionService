using SubscriptionService.Domain;

namespace SubscriptionService.Application;

public interface IEventDeliveryClient
{
    Task<DeliveryOutcome> DeliverAsync(
        SubscriptionDefinition subscription,
        SubscriptionEvent @event,
        CancellationToken cancellationToken = default);
}
