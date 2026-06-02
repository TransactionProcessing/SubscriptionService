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
                            "Subscription {SubscriptionId} paused after event {EventId} was parked",
                            subscription.SubscriptionId,
                            @event.EventId);
                        paused = true;
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
