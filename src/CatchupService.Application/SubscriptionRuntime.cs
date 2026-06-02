using CatchupService.Domain;
using Microsoft.Extensions.Logging;

namespace CatchupService.Application;

public sealed class SubscriptionRuntime(
    IEventDeliveryClient deliveryClient,
    ICheckpointStore checkpointStore,
    IParkedEventStore parkedEventStore,
    IReplaySessionStore replaySessionStore,
    ILogger<SubscriptionRuntime> logger)
{
    private readonly SemaphoreSlim _replayGate = new(1, 1);

    public async Task<bool> DeliverLiveAsync(
        SubscriptionDefinition subscription,
        SubscriptionEvent @event,
        CancellationToken cancellationToken = default)
    {
        await _replayGate.WaitAsync(cancellationToken);
        try
        {
            return await DeliverInternalAsync(subscription, @event, isReplay: false, cancellationToken);
        }
        finally
        {
            _replayGate.Release();
        }
    }

    public async Task ReplayAsync(
        SubscriptionDefinition subscription,
        CancellationToken cancellationToken = default)
    {
        await _replayGate.WaitAsync(cancellationToken);
        try
        {
            logger.LogInformation("Starting replay for subscription {SubscriptionId}", subscription.SubscriptionId);
            var session = await replaySessionStore.StartAsync(subscription.SubscriptionId, cancellationToken);
            var parkedEvents = await parkedEventStore.GetParkedEventsAsync(subscription.SubscriptionId, cancellationToken);

            foreach (var parkedEvent in parkedEvents.OrderBy(x => x.SequenceNumber))
            {
                var replayEvent = new SubscriptionEvent(
                    parkedEvent.EventId,
                    parkedEvent.SubscriptionId,
                    subscription.SecondaryIndexName,
                    parkedEvent.SequenceNumber,
                    parkedEvent.StreamName,
                    parkedEvent.EventType,
                    parkedEvent.Payload,
                    parkedEvent.ContentType,
                    parkedEvent.Metadata,
                    parkedEvent.OccurredAt);

                await DeliverInternalAsync(subscription, replayEvent, isReplay: true, cancellationToken);
            }

            await replaySessionStore.CompleteAsync(session.ReplaySessionId, cancellationToken);
            logger.LogInformation("Completed replay for subscription {SubscriptionId}", subscription.SubscriptionId);
        }
        finally
        {
            _replayGate.Release();
        }
    }

    private async Task<bool> DeliverInternalAsync(
        SubscriptionDefinition subscription,
        SubscriptionEvent @event,
        bool isReplay,
        CancellationToken cancellationToken)
    {
        var attempts = 0;
        var maxAttempts = Math.Max(1, subscription.Retry.MaxAttempts);
        string? failureReason = null;

        while (attempts < maxAttempts)
        {
            attempts++;
            try
            {
                var result = await deliveryClient.DeliverAsync(subscription, @event, cancellationToken);
                if (result.IsSuccess)
                {
                    logger.LogInformation(
                        "Delivered event {EventId} for subscription {SubscriptionId} on attempt {Attempt}",
                        @event.EventId,
                        subscription.SubscriptionId,
                        attempts);

                    if (!isReplay)
                    {
                        await checkpointStore.SaveCheckpointAsync(subscription.SubscriptionId, @event.SequenceNumber, cancellationToken);
                    }

                    return true;
                }

                failureReason = result.FailureReason ?? "Delivery returned a non-success response.";
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failureReason = ex.Message;
                logger.LogWarning(
                    ex,
                    "Delivery attempt {Attempt} failed for event {EventId} in subscription {SubscriptionId}",
                    attempts,
                    @event.EventId,
                    subscription.SubscriptionId);
            }

            if (attempts < maxAttempts)
            {
                logger.LogWarning(
                    "Retrying event {EventId} for subscription {SubscriptionId} after failure: {FailureReason}",
                    @event.EventId,
                    subscription.SubscriptionId,
                    failureReason);

                await Task.Delay(subscription.Retry.Delay, cancellationToken);
            }
        }

        var parkedEvent = ParkedEvent.FromEvent(@event, failureReason ?? "Delivery failed.", attempts);
        await parkedEventStore.ParkAsync(parkedEvent, cancellationToken);
        logger.LogWarning(
            "Parked event {EventId} for subscription {SubscriptionId} after {Attempts} attempts",
            @event.EventId,
            subscription.SubscriptionId,
            attempts);

        return false;
    }
}
