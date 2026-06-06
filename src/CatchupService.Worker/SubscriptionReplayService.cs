using CatchupService.Application;
using CatchupService.Domain;

namespace CatchupService.Worker;

public sealed class SubscriptionReplayService(
    ISubscriptionConfigurationStore configurationStore,
    IEventDeliveryClient deliveryClient,
    ICheckpointStore checkpointStore,
    IParkedEventStore parkedEventStore,
    IReplaySessionStore replaySessionStore,
    ILoggerFactory loggerFactory,
    WorkerRuntimeRegistry runtimeRegistry,
    ILogger<SubscriptionReplayService> logger)
    : ISubscriptionReplayService
{
    public async Task<ReplayOperationResult> ReplayAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        var subscriptions = await configurationStore.GetSubscriptionsAsync(cancellationToken);
        var subscription = subscriptions.FirstOrDefault(x => string.Equals(x.SubscriptionId, subscriptionId, StringComparison.OrdinalIgnoreCase));

        if (subscription is null)
        {
            return new ReplayOperationResult(subscriptionId, false, "Subscription not found.", WasQueued: false, UsedLiveRuntime: false);
        }

        var runtime = runtimeRegistry.Get(subscriptionId);
        if (runtime is null)
        {
            logger.LogInformation("Replay requested for subscription {SubscriptionId} with no live runtime; creating an on-demand runtime.", subscriptionId);
            runtime = CreateOnDemandRuntime();
        }

        _ = Task.Run(() => runtime.ReplayAsync(subscription, cancellationToken), cancellationToken);
        return new ReplayOperationResult(subscriptionId, true, "Replay started.", WasQueued: true, UsedLiveRuntime: runtimeRegistry.Get(subscriptionId) is not null);
    }

    public async Task<ReplayStatus?> GetStatusAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        var subscriptions = await configurationStore.GetSubscriptionsAsync(cancellationToken);
        if (!subscriptions.Any(x => string.Equals(x.SubscriptionId, subscriptionId, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var activeSessions = await replaySessionStore.GetActiveSessionsAsync(subscriptionId, cancellationToken);
        return new ReplayStatus(subscriptionId, activeSessions.Count > 0, activeSessions.Count);
    }

    private SubscriptionRuntime CreateOnDemandRuntime() => new(
        deliveryClient,
        checkpointStore,
        parkedEventStore,
        replaySessionStore,
        configurationStore,
        loggerFactory.CreateLogger<SubscriptionRuntime>());
}