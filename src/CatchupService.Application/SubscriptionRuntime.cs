using CatchupService.Domain;
using Microsoft.Extensions.Logging;

namespace CatchupService.Application;

public sealed class SubscriptionRuntime(
    IEventDeliveryClient deliveryClient,
    ICheckpointStore checkpointStore,
    IParkedEventStore parkedEventStore,
    IReplaySessionStore replaySessionStore,
    ISubscriptionConfigurationStore configurationStore,
    ILogger<SubscriptionRuntime> logger)
{
    public Task InitializeAsync(CheckpointState checkpoint)
    {
        // Initialize runtime's persistent checkpoint state so processed totals and commit position
        // are available immediately (useful after restarts before any deliveries occur).
        _lastSavedCommitPosition = checkpoint.CommitPosition;
        _totalProcessed = checkpoint.ProcessedCount;
        _lastSavedProcessed = checkpoint.ProcessedCount;
        _checkpointInitialized = true;
        logger.LogDebug("Runtime initialized from checkpoint commit={Commit} processed={Processed}", _lastSavedCommitPosition, _totalProcessed);
        return Task.CompletedTask;
    }

    private readonly SemaphoreSlim _replayGate = new(1, 1);
    private bool _checkpointInitialized;
    private long _totalProcessed;
    private long _lastSavedProcessed;
    private long? _lastSavedCommitPosition;

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

            foreach (var parkedEvent in parkedEvents.OrderBy(x => x.ParkedAt))
            {
                var replayEvent = new SubscriptionEvent(
                    parkedEvent.EventId,
                    parkedEvent.SubscriptionId,
                    subscription.SecondaryIndexName,
                    parkedEvent.StreamName,
                    parkedEvent.EventType,
                    parkedEvent.Payload,
                    parkedEvent.ContentType,
                    parkedEvent.Metadata,
                    parkedEvent.OccurredAt,
                    null);

                var delivered = await DeliverInternalAsync(subscription, replayEvent, isReplay: true, cancellationToken);
                if (delivered)
                {
                    // If replayed parked event was delivered successfully, remove or soft-delete it per configuration
                    await parkedEventStore.RemoveParkedEventAsync(subscription.SubscriptionId, parkedEvent.ParkedEventId, configurationStore, cancellationToken);
                }
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
        if (!subscription.Enabled)
        {
            logger.LogInformation("Ignoring event {EventId} for disabled subscription {SubscriptionId}", @event.EventId, subscription.SubscriptionId);
            return false;
        }
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
                        // Initialize last saved sequence and processed counts from persistent store on first delivery
                        if (!_checkpointInitialized)
                        {
                            try
                            {
                                var state = await checkpointStore.GetCheckpointAsync(subscription.SubscriptionId, cancellationToken);
                                _lastSavedCommitPosition = state.CommitPosition;
                                _totalProcessed = state.ProcessedCount;
                                _lastSavedProcessed = state.ProcessedCount;
                                _checkpointInitialized = true;
                                logger.LogDebug("Initialized checkpoint for {SubscriptionId} to commitPosition {Commit} and processed {Processed}", subscription.SubscriptionId, _lastSavedCommitPosition, _totalProcessed);
                            }
                            catch (Exception ex)
                            {
                                logger.LogWarning(ex, "Failed to initialize checkpoint for {SubscriptionId}", subscription.SubscriptionId);
                                _checkpointInitialized = true;
                            }
                        }

                        var batchSize = Math.Max(1, subscription.Checkpoint.BatchSize);
                        // Increment total processed and persist when we've advanced by at least batchSize since last saved processed count
                        _totalProcessed++;
                        if (_totalProcessed - _lastSavedProcessed >= batchSize)
                        {
                            try
                            {
                                if (!isReplay)
                                {
                                    await checkpointStore.SaveCheckpointAsync(subscription.SubscriptionId, @event.CommitPosition, _totalProcessed, "batch-save", cancellationToken);
                                    _lastSavedCommitPosition = @event.CommitPosition;
                                }
                                else
                                {
                                    // For replay, do not advance commit; persist processed count only
                                    await checkpointStore.SaveCheckpointAsync(subscription.SubscriptionId, _lastSavedCommitPosition, _totalProcessed, "replay-save", cancellationToken);
                                }

                                logger.LogInformation("Saved checkpoint for {SubscriptionId} with processed {Processed}", subscription.SubscriptionId, _totalProcessed);
                                _lastSavedCommitPosition = @event.CommitPosition;
                                _lastSavedProcessed = _totalProcessed;
                            }
                            catch (Exception ex)
                            {
                                logger.LogError(ex, "Failed to save checkpoint for {SubscriptionId} at commitPosition {Commit}", subscription.SubscriptionId, @event.CommitPosition);
                            }
                        }
                        else
                        {
                            logger.LogDebug("Checkpoint for {SubscriptionId} not due yet (lastSavedCommit={LastSavedCommit} lastSavedProcessed={LastSavedProcessed} totalProcessed={Total} batchSize={BatchSize})", subscription.SubscriptionId, _lastSavedCommitPosition, _lastSavedProcessed, _totalProcessed, batchSize);
                        }
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
