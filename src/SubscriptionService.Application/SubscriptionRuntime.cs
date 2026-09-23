using Microsoft.Extensions.Logging;
using SubscriptionService.Domain;

namespace SubscriptionService.Application;

public sealed class SubscriptionRuntime(
    IEventDeliveryClient deliveryClient,
    ICheckpointStore checkpointStore,
    IParkedEventStore parkedEventStore,
    IReplaySessionStore replaySessionStore,
    ISubscriptionConfigurationStore configurationStore,
    ILogger<SubscriptionRuntime> logger,
    ISubscriptionEventLogStore? eventLogStore = null)
{
    private readonly object _stateLock = new();
    private SubscriptionDeliverySnapshot? _lastDeliverySnapshot;

    public Task InitializeAsync(CheckpointState checkpoint)
    {
        // Initialize runtime state so restart resumes from the exact persisted checkpoint.
        this._lastSavedCommitPosition = checkpoint.CommitPosition;
        this._lastSavedPreparePosition = checkpoint.PreparePosition;
        this._totalProcessed = checkpoint.ProcessedCount;
        this._lastSavedProcessed = checkpoint.ProcessedCount;
        this._checkpointInitialized = true;
        logger.LogDebug(
            "Runtime initialized from checkpoint commit={Commit} prepare={Prepare} processed={Processed}",
            this._lastSavedCommitPosition,
            this._lastSavedPreparePosition,
            this._totalProcessed);
        return Task.CompletedTask;
    }

    public SubscriptionDeliverySnapshot? GetLastDeliverySnapshot()
    {
        lock (this._stateLock)
        {
            return this._lastDeliverySnapshot;
        }
    }

    public long GetProcessedCount()
    {
        lock (this._stateLock)
        {
            return this._totalProcessed;
        }
    }

    private readonly SemaphoreSlim _replayGate = new(1, 1);
    private bool _checkpointInitialized;
    private long _totalProcessed;
    private long _lastSavedProcessed;
    private long? _lastSavedCommitPosition;
    private long? _lastSavedPreparePosition;

    public async Task<DeliveryOutcome> DeliverLiveAsync(
        SubscriptionDefinition subscription,
        SubscriptionEvent @event,
        CancellationToken cancellationToken = default)
    {
        await this._replayGate.WaitAsync(cancellationToken);
        try
        {
            logger.LogTrace(
                "Live delivery received for {SubscriptionId}: event {EventId} stream {StreamName} commit {CommitPosition}",
                subscription.SubscriptionId,
                @event.EventId,
                @event.StreamName,
                @event.CommitPosition);
            return await this.DeliverInternalAsync(subscription, @event, isReplay: false, cancellationToken);
        }
        finally
        {
            this._replayGate.Release();
        }
    }

    public async Task ReplayAsync(
        SubscriptionDefinition subscription,
        CancellationToken cancellationToken = default)
    {
        await this._replayGate.WaitAsync(cancellationToken);
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
                    null,
                    null);

                // Treat the parked row as the source-of-truth snapshot for replay.
                // Remove it first so a failed replay replaces it with one fresh parked row
                // instead of leaving the original row active and stacking duplicates.
                await parkedEventStore.RemoveParkedEventAsync(subscription.SubscriptionId, parkedEvent.ParkedEventId, configurationStore, cancellationToken);

                await this.DeliverInternalAsync(subscription, replayEvent, isReplay: true, cancellationToken);
            }

            await replaySessionStore.CompleteAsync(session.ReplaySessionId, cancellationToken);
            logger.LogInformation("Completed replay for subscription {SubscriptionId}", subscription.SubscriptionId);
        }
        finally
        {
            this._replayGate.Release();
        }
    }

    private async Task<DeliveryOutcome> DeliverInternalAsync(
        SubscriptionDefinition subscription,
        SubscriptionEvent @event,
        bool isReplay,
        CancellationToken cancellationToken)
    {
        if (!subscription.Enabled && !isReplay)
        {
            logger.LogInformation("Ignoring event {EventId} for disabled subscription {SubscriptionId}", @event.EventId, subscription.SubscriptionId);
            return DeliveryOutcome.Failure("Subscription is disabled.");
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
                await this.LogAttemptAsync(subscription, @event, result, attempts, isReplay, cancellationToken);
                if (result.IsSuccess)
                {
                    this.SetLastDeliverySnapshot(@event, "Delivered", null);
                    logger.LogInformation(
                        "Delivered event {EventId} for subscription {SubscriptionId} on attempt {Attempt}",
                        @event.EventId,
                        subscription.SubscriptionId,
                        attempts);

                    // Initialize the persisted checkpoint once so both live delivery and replay
                    // share the same accumulated processed count.
                    if (!this._checkpointInitialized)
                    {
                        try
                        {
                            var state = await checkpointStore.GetCheckpointAsync(subscription.SubscriptionId, cancellationToken);
                            this._lastSavedCommitPosition = state.CommitPosition;
                            this._lastSavedPreparePosition = state.PreparePosition;
                            this._totalProcessed = state.ProcessedCount;
                            this._lastSavedProcessed = state.ProcessedCount;
                            this._checkpointInitialized = true;
                            logger.LogDebug(
                                "Initialized checkpoint for {SubscriptionId} to commitPosition {Commit} preparePosition {Prepare} and processed {Processed}",
                                subscription.SubscriptionId,
                                this._lastSavedCommitPosition,
                                this._lastSavedPreparePosition,
                                this._totalProcessed);
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Failed to initialize checkpoint for {SubscriptionId}", subscription.SubscriptionId);
                            this._checkpointInitialized = true;
                        }
                    }

                    var batchSize = Math.Max(1, subscription.Checkpoint.BatchSize);
                    this._totalProcessed++;
                    logger.LogTrace(
                        "Processed event for {SubscriptionId}: event {EventId} totalProcessed {TotalProcessed} batchSize {BatchSize} replay={IsReplay}",
                        subscription.SubscriptionId,
                        @event.EventId,
                        this._totalProcessed,
                        batchSize,
                        isReplay);

                    if (this._totalProcessed - this._lastSavedProcessed >= batchSize)
                    {
                        try
                        {
                            var checkpointCommitPosition = isReplay ? this._lastSavedCommitPosition : @event.CommitPosition;
                            var checkpointPreparePosition = isReplay ? this._lastSavedPreparePosition : @event.PreparePosition;
                            var checkpointReason = isReplay ? "replay-save" : "batch-save";

                            await checkpointStore.SaveCheckpointAsync(
                                subscription.SubscriptionId,
                                checkpointCommitPosition,
                                this._totalProcessed,
                                checkpointReason,
                                checkpointPreparePosition,
                                cancellationToken);

                            if (!isReplay)
                            {
                                this._lastSavedCommitPosition = @event.CommitPosition;
                                this._lastSavedPreparePosition = @event.PreparePosition;
                            }

                            logger.LogInformation("Saved checkpoint for {SubscriptionId} with processed {Processed}", subscription.SubscriptionId, this._totalProcessed);
                            this._lastSavedProcessed = this._totalProcessed;
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Failed to save checkpoint for {SubscriptionId} at commitPosition {Commit}", subscription.SubscriptionId, @event.CommitPosition);
                        }
                    }
                    else
                    {
                        logger.LogDebug(
                            "Checkpoint for {SubscriptionId} not due yet (lastSavedCommit={LastSavedCommit} lastSavedProcessed={LastSavedProcessed} totalProcessed={Total} batchSize={BatchSize} replay={IsReplay})",
                            subscription.SubscriptionId,
                            this._lastSavedCommitPosition,
                            this._lastSavedProcessed,
                            this._totalProcessed,
                            batchSize,
                            isReplay);
                    }

                    return DeliveryOutcome.Success;
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
                await this.LogAttemptAsync(
                    subscription,
                    @event,
                    DeliveryOutcome.Failure(failureReason),
                    attempts,
                    isReplay,
                    cancellationToken);
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
        this.SetLastDeliverySnapshot(@event, "Failed", failureReason);
        await parkedEventStore.ParkAsync(parkedEvent, cancellationToken);
        logger.LogWarning(
            "Parked event {EventId} for subscription {SubscriptionId} after {Attempts}",
            @event.EventId,
            subscription.SubscriptionId,
            attempts);

        return DeliveryOutcome.Failure(failureReason ?? "Delivery failed.");
    }

    public async Task<DeliveryOutcome> DeliverReplayAsync(
        SubscriptionDefinition subscription,
        SubscriptionEvent @event,
        CancellationToken cancellationToken = default)
    {
        await this._replayGate.WaitAsync(cancellationToken);
        try
        {
            return await this.DeliverInternalAsync(subscription, @event, isReplay: true, cancellationToken);
        }
        finally
        {
            this._replayGate.Release();
        }
    }

    private async Task LogAttemptAsync(
        SubscriptionDefinition subscription,
        SubscriptionEvent @event,
        DeliveryOutcome outcome,
        int deliveryAttempt,
        bool isReplay,
        CancellationToken cancellationToken)
    {
        if (!subscription.EnableEventLogging || eventLogStore is null)
        {
            return;
        }

        try
        {
            await eventLogStore.AddAsync(
                new SubscriptionEventLog(
                    @event.EventId,
                    subscription.SubscriptionId,
                    System.Text.Encoding.UTF8.GetString(@event.Payload),
                    subscription.EndpointUrl,
                    outcome.ResponseStatusCode,
                    @event.EventType,
                    outcome.ResponseBody,
                    DateTimeOffset.UtcNow,
                    deliveryAttempt,
                    isReplay),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to log delivery attempt {Attempt} for event {EventId} in subscription {SubscriptionId}", deliveryAttempt, @event.EventId, subscription.SubscriptionId);
        }
    }

    private void SetLastDeliverySnapshot(SubscriptionEvent @event, string status, string? failureReason)
    {
        lock (this._stateLock)
        {
            this._lastDeliverySnapshot = new SubscriptionDeliverySnapshot(
                @event.EventId,
                @event.StreamName,
                @event.EventType,
                DateTimeOffset.UtcNow,
                status,
                failureReason);
        }
    }
}
