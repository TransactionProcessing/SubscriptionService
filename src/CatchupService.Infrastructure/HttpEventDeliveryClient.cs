using System.Net.Http.Headers;
using CatchupService.Application;
using CatchupService.Domain;

namespace CatchupService.Infrastructure;

public sealed class HttpEventDeliveryClient(HttpClient httpClient) : IEventDeliveryClient
{
    public async Task<DeliveryOutcome> DeliverAsync(
        SubscriptionDefinition subscription,
        SubscriptionEvent @event,
        CancellationToken cancellationToken = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(subscription.Timeout.RequestTimeout);

        using var request = new HttpRequestMessage(HttpMethod.Post, subscription.Endpoint)
        {
            Content = new ByteArrayContent(@event.Payload)
        };

        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(@event.ContentType);
        AddHeaders(request, subscription, @event);

        using var response = await httpClient.SendAsync(request, timeoutCts.Token);
        return response.IsSuccessStatusCode
            ? DeliveryOutcome.Success
            : DeliveryOutcome.Failure($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}".Trim());
    }

    private static void AddHeaders(HttpRequestMessage request, SubscriptionDefinition subscription, SubscriptionEvent @event)
    {
        request.Headers.TryAddWithoutValidation("eventType", @event.EventType);
        request.Headers.TryAddWithoutValidation("eventHandlerType", subscription.Tag);
        request.Headers.TryAddWithoutValidation("X-Catchup-Subscription-Id", subscription.SubscriptionId);

        if (@event.Metadata is not null)
        {
            foreach (var kvp in @event.Metadata)
            {
                request.Headers.TryAddWithoutValidation($"X-Catchup-Meta-{kvp.Key}", kvp.Value);
            }
        }
    }
}
