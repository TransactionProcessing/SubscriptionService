using SubscriptionService.Domain;

namespace SubscriptionService.Worker;

public sealed record DailyCommitPositionBackfillResult(
    string SubscriptionId,
    bool Started,
    string Message,
    int DaysBackfilled = 0);

public sealed class DailyCommitPositionBackfillService(
    ISubscriptionConfigurationStore configurationStore,
    ISubscriptionEventSource eventSource,
    IDailyCommitPositionStore dailyCommitPositionStore,
    ILogger<DailyCommitPositionBackfillService> logger)
{
    public async Task<DailyCommitPositionBackfillResult> BackfillAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default)
    {
        var subscriptions = await configurationStore.GetSubscriptionsAsync(cancellationToken);
        var subscription = subscriptions.FirstOrDefault(x =>
            string.Equals(x.SubscriptionId, subscriptionId, StringComparison.OrdinalIgnoreCase));

        if (subscription is null)
        {
            return new(subscriptionId, false, "Subscription not found.");
        }

        if (string.IsNullOrWhiteSpace(subscription.SecondaryIndexName))
        {
            return new(subscriptionId, false, "The subscription has no secondary index.");
        }

        var recordedDates = new HashSet<DateTime>();
        var daysBackfilled = 0;

        await eventSource.ReadFromBeginningAsync(subscription, async (@event, ct) =>
        {
            var date = @event.OccurredAt.UtcDateTime.Date;
            if (!recordedDates.Add(date))
            {
                return;
            }

            var existing = await dailyCommitPositionStore.GetCommitPositionForDateAsync(
                subscription.SubscriptionId,
                subscription.SecondaryIndexName,
                date,
                ct);

            if (existing is null)
            {
                await dailyCommitPositionStore.UpsertAsync(
                    subscription.SubscriptionId,
                    subscription.SecondaryIndexName,
                    date,
                    @event.CommitPosition,
                    ct);
                daysBackfilled++;
            }
        }, cancellationToken);

        logger.LogInformation(
            "Backfilled {DaysBackfilled} daily commit positions for subscription {SubscriptionId}",
            daysBackfilled,
            subscriptionId);

        return new(subscriptionId, true, $"Backfill completed. {daysBackfilled} daily commit positions added.", daysBackfilled);
    }
}
