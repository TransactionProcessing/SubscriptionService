using CatchupService.Domain;
using Microsoft.Extensions.Logging;

namespace CatchupService.Application;

public sealed class SubscriptionPoller(
    ISubscriptionEventSource eventSource,
    ICheckpointStore checkpointStore,
    SubscriptionRuntime runtime,
    TimeSpan resubscribeDelay,
    ILogger<SubscriptionPoller> logger)
{
    public async Task RunAsync(SubscriptionDefinition subscription, CancellationToken cancellationToken = default)
    {
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
                    if (!delivered)
                    {
                        logger.LogWarning(
                            "Subscription {SubscriptionId} parked event {EventId}",
                            subscription.SubscriptionId,
                            @event.EventId);

                        if (!subscription.ContinueOnParked)
                        {
                            paused = true;
                            return false;
                        }

                        // If configured to continue on parked, return true so subscription keeps processing
                        return true;
                    }

                    return delivered;
                },
                cancellationToken);

            if (paused || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await Task.Delay(resubscribeDelay, cancellationToken);
        }
    }
}
