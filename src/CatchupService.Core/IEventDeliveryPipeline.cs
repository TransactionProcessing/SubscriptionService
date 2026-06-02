namespace CatchupService.Core;

public interface IEventDeliveryPipeline
{
    Task<DeliveryResult> DeliverAsync(SubscriptionEvent subscriptionEvent, CancellationToken cancellationToken = default);
}
