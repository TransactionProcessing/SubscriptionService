using CatchupService.Domain;

namespace CatchupService.Application;

public sealed class SubscriptionStatusService(
    ISubscriptionConfigurationStore configurationStore,
    ICheckpointStore checkpointStore,
    IParkedEventStore parkedEventStore,
    IReplaySessionStore replaySessionStore,
    IRunningSubscriptionRegistry runningSubscriptionRegistry,
    IStreamEventCountStore streamEventCountStore)
    : ISubscriptionStatusService
{
    public async Task<IReadOnlyCollection<SubscriptionStatus>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var subscriptions = await configurationStore.GetSubscriptionsAsync(cancellationToken);
        var statuses = new List<SubscriptionStatus>(subscriptions.Count);

        foreach (var subscription in subscriptions.OrderBy(x => x.SubscriptionId))
        {
            var status = await BuildStatusAsync(subscription.SubscriptionId, cancellationToken);
            if (status is not null)
            {
                statuses.Add(status);
            }
        }

        return statuses;
    }

    public async Task<SubscriptionStatus?> GetAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        var subscriptions = await configurationStore.GetSubscriptionsAsync(cancellationToken);
        if (!subscriptions.Any(x => string.Equals(x.SubscriptionId, subscriptionId, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        return await BuildStatusAsync(subscriptionId, cancellationToken);
    }

    private async Task<SubscriptionStatus?> BuildStatusAsync(string subscriptionId, CancellationToken cancellationToken)
    {
        var subscriptions = await configurationStore.GetSubscriptionsAsync(cancellationToken);
        var subscription = subscriptions.FirstOrDefault(x => string.Equals(x.SubscriptionId, subscriptionId, StringComparison.OrdinalIgnoreCase));
        if (subscription is null)
        {
            return null;
        }

        var checkpoint = await checkpointStore.GetCheckpointAsync(subscriptionId, cancellationToken);
        var parkedEvents = await parkedEventStore.GetParkedEventsAsync(subscriptionId, cancellationToken);
        var replaySessions = await replaySessionStore.GetActiveSessionsAsync(subscriptionId, cancellationToken);
        var latestParkedEvent = parkedEvents.OrderByDescending(x => x.ParkedAt).FirstOrDefault();
        var isRunning = runningSubscriptionRegistry.IsRunning(subscriptionId);
        var hasReplaySession = replaySessions.Count > 0;
        var health = GetHealth(isRunning, hasReplaySession, parkedEvents.Count);

        var processed = checkpoint.ProcessedCount;
        long totalEvents = 0;
        if (!string.IsNullOrWhiteSpace(subscription.SecondaryIndexName))
        {
            var total = await streamEventCountStore.GetTotalForIndexAsync(subscription.SecondaryIndexName, cancellationToken);
            totalEvents = total ?? 0L;
        }

        double percent = 0;
        if (totalEvents > 0)
        {
            percent = Math.Min(100.0, (double)processed / totalEvents * 100.0);
        }

        string? display = totalEvents > 0 ? $"{percent:0.##}% ({processed}/{totalEvents})" : null;

        return new SubscriptionStatus(
            subscriptionId,
            subscription.SecondaryIndexName,
            subscription.EndpointUrl,
            subscription.Tag,
            isRunning,
            health,
            null,
            checkpoint.CommitPosition,
            checkpoint.CheckpointReason,
            parkedEvents.Count,
            latestParkedEvent?.ParkedAt,
            latestParkedEvent?.FailureReason,
            hasReplaySession,
            processed,
            totalEvents,
            percent,
            display);
    }

    private static string GetHealth(bool isRunning, bool hasReplaySession, int parkedEventCount) =>
        hasReplaySession
            ? "Replaying"
            : !isRunning
                ? "Stopped"
                : "Healthy";
}