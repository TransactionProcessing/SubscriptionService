using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Threading.Channels;
using CatchupService.Contracts;
using CatchupService.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CatchupService.Application;

public sealed class SubscriptionProcessor(
    ISubscriptionEventReader eventReader,
    IEventDeliveryPipeline deliveryPipeline,
    ICheckpointStore checkpointStore,
    IParkedEventStore parkedEventStore,
    IOptions<SubscriptionRuntimeOptions> options,
    ILogger<SubscriptionProcessor> logger)
{
    public Task RunAsync(SubscriptionDefinition subscription, CancellationToken cancellationToken = default) =>
        RunSubscriptionAsync(subscription, cancellationToken, isReplay: false);

    public Task RunReplayAsync(SubscriptionDefinition subscription, IAsyncEnumerable<SubscriptionEvent> replayStream, CancellationToken cancellationToken = default) =>
        RunStreamAsync(subscription with { }, replayStream, cancellationToken, isReplay: true);

    private async Task RunSubscriptionAsync(SubscriptionDefinition subscription, CancellationToken cancellationToken, bool isReplay)
    {
        var checkpoint = await checkpointStore.GetAsync(subscription.Name, cancellationToken);
        var stream = isReplay
            ? eventReader.ReadAsync(subscription, null, cancellationToken)
            : eventReader.ReadAsync(subscription, checkpoint, cancellationToken);

        await RunStreamAsync(subscription, stream, cancellationToken, isReplay);
    }

    private async Task RunStreamAsync(
        SubscriptionDefinition subscription,
        IAsyncEnumerable<SubscriptionEvent> stream,
        CancellationToken cancellationToken,
        bool isReplay = false)
    {
        var queueCapacity = Math.Max(1, subscription.QueueCapacity > 0 ? subscription.QueueCapacity : options.Value.DefaultQueueCapacity);
        var channel = Channel.CreateBounded<SubscriptionEvent>(new BoundedChannelOptions(queueCapacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });

        var producer = Task.Run(async () =>
        {
            try
            {
                await foreach (var item in stream.WithCancellation(cancellationToken))
                {
                    await channel.Writer.WriteAsync(item with { IsReplay = isReplay }, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }, cancellationToken);

        await ConsumeAsync(subscription, channel.Reader, cancellationToken);
        await producer;
    }

    private async Task ConsumeAsync(
        SubscriptionDefinition subscription,
        ChannelReader<SubscriptionEvent> reader,
        CancellationToken cancellationToken)
    {
        SubscriptionCheckpoint? checkpoint = null;
        await foreach (var subscriptionEvent in reader.ReadAllAsync(cancellationToken))
        {
            var delivered = await DeliverWithRetryAsync(subscription, subscriptionEvent, cancellationToken);
            if (delivered.Succeeded)
            {
                checkpoint = new SubscriptionCheckpoint(subscription.Name, subscriptionEvent.Position, DateTimeOffset.UtcNow);
                await checkpointStore.SaveAsync(checkpoint, cancellationToken);
                continue;
            }

            await parkedEventStore.StoreAsync(
                new ParkedSubscriptionEvent(
                    subscription.Name,
                    subscriptionEvent.Position,
                    delivered.ErrorMessage ?? "Delivery failed",
                    subscriptionEvent.Payload.ToArray(),
                    subscriptionEvent.Headers,
                    DateTimeOffset.UtcNow),
                cancellationToken);
        }
    }

    private async Task<DeliveryResult> DeliverWithRetryAsync(
        SubscriptionDefinition subscription,
        SubscriptionEvent subscriptionEvent,
        CancellationToken cancellationToken)
    {
        var attempts = Math.Max(1, subscription.MaxDeliveryAttempts > 0 ? subscription.MaxDeliveryAttempts : options.Value.MaxDeliveryAttempts);
        var retryDelay = subscription.RetryDelay != default ? subscription.RetryDelay : options.Value.RetryDelay;

        DeliveryResult? result = null;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            result = await deliveryPipeline.DeliverAsync(subscriptionEvent, cancellationToken);
            if (result.Succeeded)
            {
                return result;
            }

            if (attempt < attempts && retryDelay > TimeSpan.Zero)
            {
                await Task.Delay(retryDelay, cancellationToken);
            }
        }

        logger.LogWarning(
            "Parking event {Position} for {Subscription} after {Attempts} attempts",
            subscriptionEvent.Position,
            subscription.Name,
            attempts);

        return result ?? DeliveryResult.Failure(null, "Delivery failed");
    }
}
