using SubscriptionService.Domain;

namespace SubscriptionService.Worker;

public sealed class DailyCommitPositionPlanner(IDailyCommitPositionStore store)
{
    public async Task<IReadOnlyCollection<SubscriptionDefinition>> GetSubscriptionsMissingDateAsync(
        IReadOnlyCollection<SubscriptionDefinition> subscriptions,
        DateTime date,
        CancellationToken cancellationToken = default)
    {
        var missing = new List<SubscriptionDefinition>();
        foreach (var subscription in subscriptions)
        {
            if (string.IsNullOrWhiteSpace(subscription.SecondaryIndexName))
            {
                continue;
            }

            var existing = await store.GetCommitPositionForDateAsync(
                subscription.SubscriptionId,
                subscription.SecondaryIndexName,
                date.Date,
                cancellationToken);

            if (existing is null)
            {
                missing.Add(subscription);
            }
        }

        return missing;
    }
}
