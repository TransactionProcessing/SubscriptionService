using System.Net;
using CatchupService.Application;
using CatchupService.Domain;
using CatchupService.Infrastructure;

namespace CatchupService.Tests;

public sealed class HttpEventDeliveryClientTests
{
    [Fact]
    public async Task DeliverAsync_AddsEventMetadataHeaders_AndTreatsAny2xxAsSuccess()
    {
        var handler = new InspectingHandler(new HttpResponseMessage(HttpStatusCode.Created));
        using var client = new HttpClient(handler);
        var deliveryClient = new HttpEventDeliveryClient(client);

        var subscription = new SubscriptionDefinition(
            "sub-1",
            "index-1",
            "https://example.test/subscriptions/sub-1",
            "orders",
            new TimeoutSettings(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1)),
            new RetrySettings(3, TimeSpan.FromMilliseconds(10)),
            new CheckpointSettings(10));

        var @event = SubscriptionEvent.Create(
            "evt-1",
            subscription.SubscriptionId,
            subscription.SecondaryIndexName,
            7,
            "orders-7",
            "created",
            [1, 2, 3],
            "application/json",
            new Dictionary<string, string>
            {
                ["customer-id"] = "c-123"
            },
            new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));

        var outcome = await deliveryClient.DeliverAsync(subscription, @event);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(subscription.Endpoint, handler.RequestUri);
        Assert.Equal("sub-1", handler.Headers["X-Catchup-Subscription-Id"]);
        Assert.Equal("c-123", handler.Headers["X-Catchup-Meta-customer-id"]);
        Assert.Equal("application/json", handler.ContentType);
    }

    private sealed class InspectingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string? ContentType { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;

            foreach (var header in request.Headers)
            {
                Headers[header.Key] = string.Join(",", header.Value);
            }

            ContentType = request.Content?.Headers.ContentType?.MediaType;
            if (request.Content is not null)
            {
                _ = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            }

            return response;
        }
    }
}
