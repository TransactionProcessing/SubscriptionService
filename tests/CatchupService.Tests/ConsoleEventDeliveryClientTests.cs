using CatchupService.Domain;
using CatchupService.Infrastructure;

namespace CatchupService.Tests;

public sealed class ConsoleEventDeliveryClientTests
{
    [Fact]
    public async Task DeliverAsync_WritesEventDetailsToConsole_AndReturnsSuccess()
    {
        var originalOut = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);

        try
        {
            var deliveryClient = new ConsoleEventDeliveryClient();
            var subscription = new SubscriptionDefinition(
                "sub-1",
                "index-1",
                "https://example.test/subscriptions/sub-1",
                "orders",
                new TimeoutSettings(TimeSpan.FromSeconds(5)),
                new RetrySettings(3, TimeSpan.FromSeconds(1)),
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
            var output = writer.ToString();
            Assert.Contains("sub-1", output);
            Assert.Contains("evt-1", output);
            Assert.Contains("orders-7", output);
            Assert.Contains("created", output);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}
