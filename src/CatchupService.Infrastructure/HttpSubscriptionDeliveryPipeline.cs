using CatchupService.Core;

namespace CatchupService.Infrastructure;

public sealed class HttpSubscriptionDeliveryPipeline(HttpClient httpClient) : IEventDeliveryPipeline
{
    public async Task<DeliveryResult> DeliverAsync(SubscriptionEvent subscriptionEvent, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, subscriptionEvent.Endpoint)
        {
            Content = new ByteArrayContent(subscriptionEvent.Payload.ToArray())
        };

        foreach (var header in subscriptionEvent.Headers)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        request.Headers.TryAddWithoutValidation("X-Subscription-Name", subscriptionEvent.SubscriptionName);
        request.Headers.TryAddWithoutValidation("X-Subscription-Position", subscriptionEvent.Position.ToString());

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if ((int)response.StatusCode is >= 200 and < 300)
        {
            return DeliveryResult.Success((int)response.StatusCode);
        }

        var responseBody = response.Content is null
            ? null
            : await response.Content.ReadAsStringAsync(cancellationToken);

        return DeliveryResult.Failure((int)response.StatusCode, responseBody ?? response.ReasonPhrase ?? "HTTP delivery failed");
    }
}
