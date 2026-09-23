using System.Text.Json;
using KurrentDB.Client;
using Microsoft.Extensions.Logging;
using SubscriptionService.Domain;

namespace SubscriptionService.Infrastructure;

public sealed class KurrentSubscriptionEventSource(KurrentDBClient client, ICheckpointStore checkpointStore, ILogger<KurrentSubscriptionEventSource> logger) : ISubscriptionEventSource
{
    private const string JsonContentType = "application/json";

    public async Task SubscribeAsync(
        SubscriptionDefinition subscriptionDefinition,
        CheckpointState checkpoint,
        Func<SubscriptionEvent, CancellationToken, Task<bool>> eventAppeared,
        CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellationRegistration = cancellationToken.Register(() => completed.TrySetResult());

        var state = checkpoint;
        var processedCount = checkpoint.ProcessedCount;
        var processedSinceCheckpoint = 0L;
        var startPosition = state.CommitPosition is long cp && cp > 0
            ? state.PreparePosition is long prepare
                ? FromAll.After(new Position((ulong)cp, (ulong)prepare))
                : FromAll.After(new Position((ulong)cp, (ulong)cp))
            : FromAll.Start;

        SubscriptionFilterOptions? filterOptions = string.IsNullOrWhiteSpace(subscriptionDefinition.SecondaryIndexName)
            ? null
            : new SubscriptionFilterOptions(
                StreamFilter.Prefix(subscriptionDefinition.SecondaryIndexName)
            );

        logger.LogInformation(
            "Starting EventStore subscription for {SubscriptionId} on index {SecondaryIndexName} from commit {CommitPosition}",
            subscriptionDefinition.SubscriptionId,
            subscriptionDefinition.SecondaryIndexName,
            checkpoint.CommitPosition is null ? "start" : checkpoint.CommitPosition.Value.ToString());

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var receivedCount = 0L;
        try
        {
            await using var subscription = filterOptions is null
                ? client.SubscribeToAll(startPosition)
                : client.SubscribeToAll(startPosition, filterOptions: filterOptions);

            await foreach (var message in subscription.Messages.WithCancellation(linkedCts.Token))
            {
                if (message is StreamMessage.CaughtUp) {
                    // Caught up signal; keep the cumulative count, but restart the batch counter.
                    logger.LogInformation("Caught up subscription {SubscriptionId} at commit {Commit}", subscriptionDefinition.SubscriptionId, state.CommitPosition);
                    processedSinceCheckpoint = 0;
                    continue;
                }

                if (message is not StreamMessage.Event(var resolvedEvent))
                {
                    continue;
                }

                logger.LogTrace(
                    "EventStore delivered event for {SubscriptionId}: stream {StreamName} event {EventId} commit {CommitPosition}",
                    subscriptionDefinition.SubscriptionId,
                    resolvedEvent.OriginalStreamId,
                    resolvedEvent.OriginalEvent.EventId,
                    resolvedEvent.OriginalPosition?.CommitPosition is ulong commitPosition ? commitPosition.ToString() : "n/a");
                
                var delivered = await eventAppeared(
                    MapEvent(subscriptionDefinition, resolvedEvent),
                    linkedCts.Token);

                receivedCount++;

                if (!delivered)
                {
                    break;
                }
                // Update checkpoint state using the latest resolved commit position.
                long? commitPos = resolvedEvent.OriginalPosition?.CommitPosition is ulong u ? (long?)u : null;
                long? preparePos = resolvedEvent.OriginalPosition?.PreparePosition is ulong p ? (long?)p : null;
                state = new CheckpointState(commitPos, processedCount, null, preparePos);

                try
                {
                    var batchSize = Math.Max(1, subscriptionDefinition.Checkpoint.BatchSize);
                    processedCount++;
                    processedSinceCheckpoint++;
                    if (processedSinceCheckpoint >= batchSize)
                    {
                        // Persist the cumulative count so progress keeps increasing across batches.
                        await checkpointStore.SaveCheckpointAsync(subscriptionDefinition.SubscriptionId, state.CommitPosition, processedCount, "batch-save", state.PreparePosition, linkedCts.Token);
                        logger.LogInformation("Persisted subscription checkpoint for {SubscriptionId} at commit {Commit}", subscriptionDefinition.SubscriptionId, state.CommitPosition);
                        processedSinceCheckpoint = 0;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to persist checkpoint for {SubscriptionId}", subscriptionDefinition.SubscriptionId);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation(
                "EventStore subscription cancellation observed for {SubscriptionId} after {Elapsed}; received {ReceivedCount} events",
                subscriptionDefinition.SubscriptionId,
                stopwatch.Elapsed,
                receivedCount);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "EventStore subscription failed for {SubscriptionId} after {Elapsed}; received {ReceivedCount} events",
                subscriptionDefinition.SubscriptionId,
                stopwatch.Elapsed,
                receivedCount);
            throw;
        }
        finally
        {
            logger.LogDebug(
                "EventStore subscription ended for {SubscriptionId} after {Elapsed}; received {ReceivedCount} events; cancelled={Cancelled}",
                subscriptionDefinition.SubscriptionId,
                stopwatch.Elapsed,
                receivedCount,
                cancellationToken.IsCancellationRequested);
        }
    }

    public async Task ReadFromBeginningAsync(
        SubscriptionDefinition subscriptionDefinition,
        Func<SubscriptionEvent, CancellationToken, Task> eventAppeared,
        CancellationToken cancellationToken = default)
        => await this.ReadFromCheckpointAsync(subscriptionDefinition, new CheckpointState(null, 0, null), eventAppeared, cancellationToken);

    public async Task ReadFromCheckpointAsync(
        SubscriptionDefinition subscriptionDefinition,
        CheckpointState checkpoint,
        Func<SubscriptionEvent, CancellationToken, Task> eventAppeared,
        CancellationToken cancellationToken = default)
    {
        var filterOptions = string.IsNullOrWhiteSpace(subscriptionDefinition.SecondaryIndexName)
            ? null
            : new SubscriptionFilterOptions(StreamFilter.Prefix(subscriptionDefinition.SecondaryIndexName));

        var startPosition = checkpoint.CommitPosition is long commitPosition
            ? FromAll.After(new Position((ulong)commitPosition, (ulong)(checkpoint.PreparePosition ?? commitPosition)))
            : FromAll.Start;

        await using var subscription = filterOptions is null
            ? client.SubscribeToAll(startPosition)
            : client.SubscribeToAll(startPosition, filterOptions: filterOptions);

        await foreach (var message in subscription.Messages.WithCancellation(cancellationToken))
        {
            if (message is StreamMessage.CaughtUp)
            {
                break;
            }

            if (message is StreamMessage.Event(var resolvedEvent))
            {
                await eventAppeared(MapEvent(subscriptionDefinition, resolvedEvent), cancellationToken);
            }
        }
    }

    public async Task<IReadOnlyList<SubscriptionEvent>> PeekAsync(
        SubscriptionDefinition subscriptionDefinition,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        if (maxCount <= 0)
        {
            return Array.Empty<SubscriptionEvent>();
        }

        var events = new List<SubscriptionEvent>(maxCount);
        var startPosition = FromAll.Start;

        SubscriptionFilterOptions? filterOptions = string.IsNullOrWhiteSpace(subscriptionDefinition.SecondaryIndexName)
            ? null
            : new SubscriptionFilterOptions(
                StreamFilter.Prefix(subscriptionDefinition.SecondaryIndexName)
            );

        await using var subscription = filterOptions is null
            ? client.SubscribeToAll(startPosition)
            : client.SubscribeToAll(startPosition, filterOptions: filterOptions);

        await foreach (var message in subscription.Messages.WithCancellation(cancellationToken))
        {
            if (message is StreamMessage.CaughtUp)
            {
                break;
            }

            if (message is not StreamMessage.Event(var resolvedEvent))
            {
                continue;
            }

            events.Add(MapEvent(subscriptionDefinition, resolvedEvent));
            if (events.Count >= maxCount)
            {
                break;
            }
        }

        return events;
    }

    private static SubscriptionEvent MapEvent(SubscriptionDefinition subscription, ResolvedEvent resolvedEvent)
    {
        var originalEvent = resolvedEvent.OriginalEvent;
        var eventId = originalEvent.EventId.ToString();
        return new SubscriptionEvent(
            eventId,
            subscription.SubscriptionId,
            subscription.SecondaryIndexName,
            resolvedEvent.OriginalStreamId,
            originalEvent.EventType,
            SubscriptionPayloadBuilder.Build(originalEvent.Data, eventId),
            JsonContentType,
            ReadMetadata(originalEvent.Metadata),
            new DateTimeOffset(DateTime.SpecifyKind(originalEvent.Created, DateTimeKind.Utc)),
            resolvedEvent.OriginalPosition?.CommitPosition is ulong commitPos ? (long?)commitPos : null,
            resolvedEvent.OriginalPosition?.PreparePosition is ulong preparePos ? (long?)preparePos : null);
    }

    private static IReadOnlyDictionary<string, string> ReadMetadata(ReadOnlyMemory<byte> metadata)
    {
        if (metadata.IsEmpty)
        {
            return new Dictionary<string, string>();
        }

        try
        {
            using var document = JsonDocument.Parse(metadata);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new Dictionary<string, string>();
            }

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                values[property.Name] = property.Value.ToString();
            }

            return values;
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }
}
