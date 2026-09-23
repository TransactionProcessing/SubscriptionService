using Microsoft.Extensions.Logging;
using SubscriptionService.Domain;

namespace SubscriptionService.Application;

public sealed class SubscriptionPoller(
    ISubscriptionEventSource eventSource,
    ICheckpointStore checkpointStore,
    ISubscriptionConfigurationStore configurationStore,
    SubscriptionRuntime runtime,
    TimeSpan resubscribeDelay,
    ILogger<SubscriptionPoller> logger)
{
    public async Task RunAsync(SubscriptionDefinition subscription, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Starting live subscription loop for {SubscriptionId} on index {SecondaryIndexName}",
            subscription.SubscriptionId,
            subscription.SecondaryIndexName);

        while (!cancellationToken.IsCancellationRequested)
        {
            var checkpoint = await checkpointStore.GetCheckpointAsync(subscription.SubscriptionId, cancellationToken);
            var paused = false;

            await eventSource.SubscribeAsync(
                subscription,
                checkpoint,
                async (@event, eventCancellationToken) =>
                {
                    var delivered = await runtime.DeliverLiveAsync(subscription, @event, eventCancellationToken);
                    if (!delivered.IsSuccess)
                    {
                        logger.LogWarning(
                            "Subscription {SubscriptionId} parked event {EventId}",
                            subscription.SubscriptionId,
                            @event.EventId);

                        if (!subscription.ContinueOnParked)
                        {
                            await configurationStore.SetOperationalStateAsync(
                                subscription.SubscriptionId,
                                SubscriptionOperationalState.Faulted,
                                delivered.FailureReason,
                                eventCancellationToken);
                            paused = true;
                            return false;
                        }

                        // If configured to continue on parked, return true so subscription keeps processing
                        return true;
                    }

                    return true;
                },
                cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                logger.LogInformation(
                    "Live subscription event source stopped after cancellation for {SubscriptionId}",
                    subscription.SubscriptionId);
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(
                    "Event source completed unexpectedly for subscription {SubscriptionId}; the worker will resubscribe",
                    subscription.SubscriptionId);
            }

            if (paused || cancellationToken.IsCancellationRequested)
            {
                logger.LogInformation(
                    "Stopping live subscription loop for {SubscriptionId} (paused={Paused}, cancelled={Cancelled})",
                    subscription.SubscriptionId,
                    paused,
                    cancellationToken.IsCancellationRequested);
                return;
            }

            logger.LogTrace(
                "Live subscription loop for {SubscriptionId} completed a pass; resubscribing after {Delay}",
                subscription.SubscriptionId,
                resubscribeDelay);
            await Task.Delay(resubscribeDelay, cancellationToken);
        }
    }
}
