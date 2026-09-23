using SubscriptionService.Application;
using SubscriptionService.Domain;

namespace SubscriptionService.Worker;

public sealed class SubscriptionReplayService(
    ISubscriptionConfigurationStore configurationStore,
    IEventDeliveryClient deliveryClient,
    ICheckpointStore checkpointStore,
    IParkedEventStore parkedEventStore,
    IReplaySessionStore replaySessionStore,
    ISubscriptionEventSource eventSource,
    ISubscriptionEventLogStore subscriptionEventLogStore,
    ILoggerFactory loggerFactory,
    WorkerRuntimeRegistry runtimeRegistry,
    WorkerOptions options,
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
            runtime = this.CreateOnDemandRuntime();
        }

        _ = Task.Run(async () =>
        {
            try
            {
                // Replay is intentionally detached from the caller so a short-lived HTTP request
                // cannot cancel cleanup of parked events after the response is returned.
                await runtime.ReplayAsync(subscription, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Replay failed for subscription {SubscriptionId}", subscriptionId);
            }
        }, CancellationToken.None);
        return new ReplayOperationResult(subscriptionId, true, "Replay started.", WasQueued: true, UsedLiveRuntime: runtimeRegistry.Get(subscriptionId) is not null);
    }

    public async Task<ReplayOperationResult> ReplayFromCheckpointAsync(string subscriptionId, long commitPosition, CancellationToken cancellationToken = default)
    {
        var subscription = (await configurationStore.GetSubscriptionsAsync(cancellationToken))
            .FirstOrDefault(x => string.Equals(x.SubscriptionId, subscriptionId, StringComparison.OrdinalIgnoreCase));
        if (subscription is null)
        {
            return new ReplayOperationResult(subscriptionId, false, "Subscription not found.", false, false);
        }

        var wasRunning = runtimeRegistry.TryGetLive(subscriptionId, out var runtime);
        if (wasRunning)
        {
            var pauseStopwatch = System.Diagnostics.Stopwatch.StartNew();
            logger.LogDebug("Requesting live subscription stop for {SubscriptionId} before checkpoint replay", subscriptionId);
            await configurationStore.SetOperationalStateAsync(subscriptionId, SubscriptionOperationalState.Stopped, "Checkpoint replay in progress", cancellationToken);
            var stopped = await runtimeRegistry.RequestStopAsync(subscriptionId, options.ReplayPauseTimeout, cancellationToken);
            if (!stopped)
            {
                logger.LogWarning(
                    "Live subscription {SubscriptionId} did not stop safely within {Elapsed}; checkpoint replay was not started",
                    subscriptionId,
                    pauseStopwatch.Elapsed);
                await configurationStore.SetOperationalStateAsync(subscriptionId, SubscriptionOperationalState.Healthy, null, cancellationToken);
                return new ReplayOperationResult(
                    subscriptionId,
                    false,
                    $"The live subscription did not stop within {options.ReplayPauseTimeout.TotalSeconds:0.0} seconds, so replay was not started. Check subscription diagnostics.",
                    false,
                    true);
            }

            logger.LogInformation("Live subscription {SubscriptionId} stopped safely before checkpoint replay after {Elapsed}", subscriptionId, pauseStopwatch.Elapsed);
        }

        runtime ??= this.CreateOnDemandRuntime();
        _ = Task.Run(async () =>
        {
            try
            {
                var session = await replaySessionStore.StartAsync(subscriptionId, CancellationToken.None);
                var checkpoint = new CheckpointState(commitPosition, 0, "checkpoint-replay", commitPosition);
                await eventSource.ReadFromCheckpointAsync(subscription, checkpoint, async (@event, ct) =>
                {
                    await runtime.DeliverReplayAsync(subscription, @event, ct);
                }, CancellationToken.None);
                await replaySessionStore.CompleteAsync(session.ReplaySessionId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Checkpoint replay failed for subscription {SubscriptionId} from commit {CommitPosition}", subscriptionId, commitPosition);
            }
            finally
            {
                if (wasRunning)
                {
                    try
                    {
                        await configurationStore.SetOperationalStateAsync(subscriptionId, SubscriptionOperationalState.Healthy, null, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to restart subscription {SubscriptionId} after checkpoint replay", subscriptionId);
                    }
                }
            }
        }, CancellationToken.None);

        return new ReplayOperationResult(subscriptionId, true, $"Replay from commit {commitPosition} started.", true, wasRunning);
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
        loggerFactory.CreateLogger<SubscriptionRuntime>(),
        subscriptionEventLogStore);
}
