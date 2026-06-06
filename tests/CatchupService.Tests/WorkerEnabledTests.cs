using CatchupService.Domain;
using System.Linq;

namespace CatchupService.Tests;

public sealed class WorkerEnabledTests
{
    [Fact]
    public void Filtering_ExcludesDisabledSubscriptions()
    {
        var subs = new[]
        {
            new SubscriptionDefinition(
                "sub-disabled",
                "index-1",
                "https://example.test/subscriptions/sub-1",
                "orders",
                new TimeoutSettings(TimeSpan.FromSeconds(5)),
                new RetrySettings(3, TimeSpan.Zero),
                new CheckpointSettings(10)) { Enabled = false },
            new SubscriptionDefinition(
                "sub-enabled",
                "index-1",
                "https://example.test/subscriptions/sub-2",
                "orders",
                new TimeoutSettings(TimeSpan.FromSeconds(5)),
                new RetrySettings(3, TimeSpan.Zero),
                new CheckpointSettings(10)) { Enabled = true }
        };

        var desired = subs.Where(x => x.Enabled).ToArray();

        Assert.Single(desired);
        Assert.Equal("sub-enabled", desired[0].SubscriptionId);
    }
}
