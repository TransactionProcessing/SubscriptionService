using System.Text;
using SubscriptionService.Domain;
using SubscriptionService.Infrastructure;

namespace SubscriptionService.Tests;

public sealed class InMemorySubscriptionEventSourceTests
{
    [Fact]
    public async Task PeekAsync_ReturnsFirstMatchingEventsInCommitOrder()
    {
        var source = new InMemorySubscriptionEventSource(new[]
        {
            SubscriptionEvent.Create("event-2", "sub-1", "events-by-organisation", "stream-2", "Type2", Encoding.UTF8.GetBytes("{\"value\":2}"), "application/json", commitPosition: 20),
            SubscriptionEvent.Create("event-1", "sub-1", "events-by-organisation", "stream-1", "Type1", Encoding.UTF8.GetBytes("{\"value\":1}"), "application/json", commitPosition: 10),
            SubscriptionEvent.Create("event-3", "sub-1", "events-by-user", "stream-3", "Type3", Encoding.UTF8.GetBytes("{\"value\":3}"), "application/json", commitPosition: 15)
        });

        var subscription = new SubscriptionDefinition(
            "preview",
            "events-by-organisation",
            "https://preview.local/",
            "preview",
            TimeoutSettings.Default,
            RetrySettings.Default,
            CheckpointSettings.Default);

        var preview = await source.PeekAsync(subscription, 2);

        Assert.Collection(
            preview,
            item => Assert.Equal("event-1", item.EventId),
            item => Assert.Equal("event-2", item.EventId));
    }
}
