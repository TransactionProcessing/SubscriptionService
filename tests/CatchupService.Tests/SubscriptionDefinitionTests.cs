using CatchupService.Domain;

namespace CatchupService.Tests;

public sealed class SubscriptionDefinitionTests
{
    [Fact]
    public void DefaultEnabled_IsTrue()
    {
        var subscription = new SubscriptionDefinition(
            "sub-1",
            "index-1",
            "https://example.test/subscriptions/sub-1",
            "orders",
            new TimeoutSettings(TimeSpan.FromSeconds(5)),
            new RetrySettings(3, TimeSpan.Zero),
            new CheckpointSettings(10));

        Assert.True(subscription.Enabled);
    }
}
