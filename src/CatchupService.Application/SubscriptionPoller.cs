using CatchupService.Domain;
using Microsoft.Extensions.Logging;

namespace CatchupService.Application;

public sealed class SubscriptionPoller(
    ISubscriptionEventSource eventSource,
    ICheckpointStore checkpointStore,
    SubscriptionRuntime runtime,
    ILogger<SubscriptionPoller> logger)
{
    public async Task RunAsync(SubscriptionDefinition subscription, CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var checkpoint = await checkpointStore.GetCheckpointAsync(subscription.SubscriptionId, cancellationToken);
            var batch = await eventSource.ReadBatchAsync(
                subscription.SecondaryIndexName,
                checkpoint,
                subscription.Checkpoint.BatchSize,
                cancellationToken);

            if (batch.Count == 0)
            {
                await Task.Delay(subscription.Timeout.PollInterval, cancellationToken);
                continue;
            }

            foreach (var @event in batch.OrderBy(x => x.SequenceNumber))
            {
                var delivered = await runtime.DeliverLiveAsync(subscription, @event, cancellationToken);
                if (!delivered)
                {
                    logger.LogWarning(
                        "Subscription {SubscriptionId} paused after event {EventId} was parked",
                        subscription.SubscriptionId,
                        @event.EventId);
                    return;
                }
            }
        }
    }
}
