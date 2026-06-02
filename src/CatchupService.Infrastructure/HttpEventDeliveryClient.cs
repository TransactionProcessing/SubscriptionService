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
        request.Headers.TryAddWithoutValidation("X-Catchup-Subscription-Id", subscription.SubscriptionId);
        request.Headers.TryAddWithoutValidation("X-Catchup-Secondary-Index", subscription.SecondaryIndexName);
        request.Headers.TryAddWithoutValidation("X-Catchup-Tag", subscription.Tag);
        request.Headers.TryAddWithoutValidation("X-Catchup-Event-Id", @event.EventId);
        request.Headers.TryAddWithoutValidation("X-Catchup-Event-Type", @event.EventType);
        request.Headers.TryAddWithoutValidation("X-Catchup-Stream-Name", @event.StreamName);
        request.Headers.TryAddWithoutValidation("X-Catchup-Sequence-Number", @event.SequenceNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("X-Catchup-Occurred-At", @event.OccurredAt.ToString("O"));

        foreach (var (key, value) in @event.Metadata)
        {
            var headerName = $"X-Catchup-Meta-{SanitizeHeaderToken(key)}";
            request.Headers.TryAddWithoutValidation(headerName, value);
        }
    }

    private static string SanitizeHeaderToken(string value)
    {
        Span<char> buffer = stackalloc char[value.Length];
        var index = 0;
        foreach (var character in value)
        {
            buffer[index++] = char.IsLetterOrDigit(character) ? character : '-';
        }

        return new string(buffer[..index]);
    }
}
