using Microsoft.Extensions.Logging;
using SubscriptionService.Domain;

namespace SubscriptionService.Application;

public sealed class SubscriptionStatusService(
    ISubscriptionConfigurationStore configurationStore,
    ICheckpointStore checkpointStore,
    IParkedEventStore parkedEventStore,
    IReplaySessionStore replaySessionStore,
    IRunningSubscriptionRegistry runningSubscriptionRegistry,
    IStreamEventCountStore streamEventCountStore,
    ILogger<SubscriptionStatusService> logger)
    : ISubscriptionStatusService
{
    public async Task<IReadOnlyCollection<SubscriptionStatus>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var subscriptions = await configurationStore.GetSubscriptionsAsync(cancellationToken);
        var statuses = new List<SubscriptionStatus>(subscriptions.Count);

        foreach (var subscription in subscriptions.OrderBy(x => x.SubscriptionId))
        {
            var status = await this.BuildStatusAsync(subscription.SubscriptionId, cancellationToken);
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

        return await this.BuildStatusAsync(subscriptionId, cancellationToken);
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
        var runtimeFailure = runningSubscriptionRegistry.GetFailure(subscriptionId);
        var runtimeState = runningSubscriptionRegistry.GetState(subscriptionId);
        var hasReplaySession = replaySessions.Count > 0;
        var health = GetHealth(subscription, hasReplaySession);
        var operationalReason = subscription.OperationalState == SubscriptionOperationalState.Faulted
            ? subscription.OperationalReason ?? latestParkedEvent?.FailureReason
            : subscription.OperationalReason;

        var processed = checkpoint.ProcessedCount;
        var totalEvents = await streamEventCountStore.GetTotalForSubscriptionAsync(subscription.SubscriptionId, cancellationToken) ?? 0L;

        double percent = 0;
        if (totalEvents > 0)
        {
            percent = Math.Min(100.0, (double)processed / totalEvents * 100.0);
        }

        string? display = totalEvents > 0 ? $"{percent:0.##}% ({processed}/{totalEvents})" : null;

        logger.LogInformation(
            "Subscription status {SubscriptionId}: live processed {ProcessedCount}, stored total {TotalEvents}, progress {ProgressDisplay}",
            subscriptionId,
            processed,
            totalEvents,
            display ?? "n/a");

        return new SubscriptionStatus(
            subscriptionId,
            subscription.SecondaryIndexName,
            subscription.EndpointUrl,
            subscription.Tag,
            isRunning,
            health,
            subscription.OperationalState,
            operationalReason,
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
            display,
            runtimeFailure?.Message,
            runtimeFailure?.OccurredAt,
            runtimeState?.LastStartedAt,
            runtimeState?.LastStoppedAt,
            runtimeState?.LastStopReason,
            runtimeState?.StopCount ?? 0);
    }

    private static string GetHealth(SubscriptionDefinition subscription, bool hasReplaySession) =>
        hasReplaySession
            ? "Replaying"
            : subscription.OperationalState switch
            {
                SubscriptionOperationalState.Faulted => "Faulted",
                SubscriptionOperationalState.Stopped => "Stopped",
                _ => "Healthy"
            };
}
