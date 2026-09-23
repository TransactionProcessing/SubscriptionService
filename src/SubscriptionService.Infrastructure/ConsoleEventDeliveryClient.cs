using System.Text;
using SubscriptionService.Application;
using SubscriptionService.Domain;

namespace SubscriptionService.Infrastructure;

public sealed class ConsoleEventDeliveryClient : IEventDeliveryClient
{
    public Task<DeliveryOutcome> DeliverAsync(
        SubscriptionDefinition subscription,
        SubscriptionEvent @event,
        CancellationToken cancellationToken = default)
    {
        var payload = Encoding.UTF8.GetString(@event.Payload);
        SubscriptionLogger.WriteInfo(
            $"[test-delivery] subscription={subscription.SubscriptionId} event={@event.EventId} type={@event.EventType} stream={@event.StreamName} commit={@event.CommitPosition} payload={payload}");

        return Task.FromResult(DeliveryOutcome.Success);
    }
}
