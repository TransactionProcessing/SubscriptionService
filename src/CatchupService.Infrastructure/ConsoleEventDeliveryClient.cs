using System.Text;
using CatchupService.Application;
using CatchupService.Domain;

namespace CatchupService.Infrastructure;

public sealed class ConsoleEventDeliveryClient : IEventDeliveryClient
{
    public Task<DeliveryOutcome> DeliverAsync(
        SubscriptionDefinition subscription,
        SubscriptionEvent @event,
        CancellationToken cancellationToken = default)
    {
        //if (@event.EventType == "SupplierTypeChangedEvent") {
        //    return Task.FromResult(DeliveryOutcome.Failure("SupplierTypeChangedEvent is not supported."));
        //}
        
        var payload = Encoding.UTF8.GetString(@event.Payload);
        Console.WriteLine(
            $"[test-delivery] subscription={subscription.SubscriptionId} event={@event.EventId} type={@event.EventType} stream={@event.StreamName} commit={@event.CommitPosition} payload={payload}");

        return Task.FromResult(DeliveryOutcome.Success);
    }
}
