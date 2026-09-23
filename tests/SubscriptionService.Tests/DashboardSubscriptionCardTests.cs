using SubscriptionService.Domain;
using SubscriptionService.Worker.Ui;

namespace SubscriptionService.Tests;

public sealed class DashboardSubscriptionCardTests
{
    [Fact]
    public void DashboardSubscriptionCard_ExposesEventLoggingState()
    {
        var card = new DashboardSubscriptionCard(
            "sub-1",
            "index-1",
            "https://example.test/events",
            "orders",
            EnableEventLogging: true,
            Enabled: true,
            IsRunning: true,
            Health: "Healthy",
            OperationalState: SubscriptionOperationalState.Healthy,
            OperationalReason: null,
            HasActiveReplaySession: false,
            ParkedEventCount: 0,
            ProcessedCount: 0,
            CommitPosition: null,
            ProgressDisplay: null,
            LatestParkedAt: null,
            LatestParkedFailureReason: null);

        Assert.True(card.EnableEventLogging);
    }
}
