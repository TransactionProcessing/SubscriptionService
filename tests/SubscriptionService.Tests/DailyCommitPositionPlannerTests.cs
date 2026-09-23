using SubscriptionService.Domain;
using SubscriptionService.Worker;

namespace SubscriptionService.Tests;

public sealed class DailyCommitPositionPlannerTests
{
    [Fact]
    public async Task GetSubscriptionsMissingDateAsync_DoesNotSkipLaterSubscriptionWhenFirstAlreadyHasRow()
    {
        var date = new DateTime(2026, 9, 4);
        var subscriptions = new[]
        {
            CreateSubscription("sub-1"),
            CreateSubscription("sub-2")
        };
        var store = new FakeDailyCommitPositionStore(
            new DailyCommitPositionRecord("sub-1", "index-1", date, 100));
        var planner = new DailyCommitPositionPlanner(store);

        var missing = await planner.GetSubscriptionsMissingDateAsync(subscriptions, date);

        var subscription = Assert.Single(missing);
        Assert.Equal("sub-2", subscription.SubscriptionId);
    }

    private static SubscriptionDefinition CreateSubscription(string id) => new(
        id,
        "index-1",
        $"https://example.test/{id}",
        "orders",
        TimeoutSettings.Default,
        RetrySettings.Default,
        CheckpointSettings.Default);

    private sealed class FakeDailyCommitPositionStore(params DailyCommitPositionRecord[] rows) : IDailyCommitPositionStore
    {
        public Task UpsertAsync(string subscriptionId, string secondaryIndexName, DateTime date, long? commitPosition, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<long?> GetCommitPositionForDateAsync(string subscriptionId, string secondaryIndexName, DateTime date, CancellationToken cancellationToken = default) =>
            Task.FromResult(rows.FirstOrDefault(x =>
                x.SubscriptionId == subscriptionId &&
                x.SecondaryIndexName == secondaryIndexName &&
                x.Date == date)?.CommitPosition);

        public Task<IReadOnlyCollection<DailyCommitPositionRecord>> GetPositionsAsync(string? subscriptionId, string? secondaryIndexName, DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<DailyCommitPositionRecord>>(rows);

        public Task DeleteOlderThanAsync(DateTime threshold, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
