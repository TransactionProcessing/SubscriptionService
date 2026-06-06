using System.Text.Json;
using CatchupService.Domain;
using Microsoft.Extensions.Logging;
using KurrentDB.Client;

namespace CatchupService.Infrastructure;

public sealed class KurrentSubscriptionEventSource(KurrentDBClient client, ICheckpointStore checkpointStore, ILogger<KurrentSubscriptionEventSource> logger) : ISubscriptionEventSource
{
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
        var lastPersistedSequence = 0L;
        var processedCount = (int)checkpoint.ProcessedCount;
        var startPosition = state.CommitPosition is long cp && cp > 0
            ? FromAll.After(new Position((ulong)cp, (ulong)cp))
            : FromAll.Start;

        SubscriptionFilterOptions? filterOptions = string.IsNullOrWhiteSpace(subscriptionDefinition.SecondaryIndexName)
            ? null
            : new SubscriptionFilterOptions(
                StreamFilter.Prefix(subscriptionDefinition.SecondaryIndexName)
            );

        try
        {
            await using var subscription = filterOptions is null
                ? client.SubscribeToAll(startPosition)
                : client.SubscribeToAll(startPosition, filterOptions: filterOptions);

            await foreach (var message in subscription.Messages.WithCancellation(linkedCts.Token))
            {
                if (message is StreamMessage.CaughtUp) {
                    // Caught up signal; runtime handles checkpoint persistence including processed counts
                    logger.LogInformation("Caught up subscription {SubscriptionId} at commit {Commit}", subscriptionDefinition.SubscriptionId, state.CommitPosition);
                    lastPersistedSequence = state.CommitPosition ?? 0L;
                    processedCount = 0;
                    continue;
                }

                if (message is not StreamMessage.Event(var resolvedEvent))
                {
                    continue;
                }
                
                var delivered = await eventAppeared(
                    MapEvent(subscriptionDefinition, resolvedEvent),
                    linkedCts.Token);

                if (!delivered)
                {
                    break;
                }
                // Update checkpoint state: use event's stream sequence number and commit position when available
                var orig = resolvedEvent.OriginalEvent;
                var seq = orig.EventNumber.ToInt64();
                long? commitPos = resolvedEvent.OriginalPosition?.CommitPosition is ulong u ? (long?)u : null;
                state = new CheckpointState(commitPos, processedCount, null);

                try
                {
                    var batchSize = Math.Max(1, subscriptionDefinition.Checkpoint.BatchSize);
                    processedCount++;
                        if (processedCount >= batchSize)
                        {
                            // Persist checkpoint for subscription (commit position + processed count)
                            await checkpointStore.SaveCheckpointAsync(subscriptionDefinition.SubscriptionId, state.CommitPosition, processedCount, "batch-save", linkedCts.Token);
                            logger.LogInformation("Persisted subscription checkpoint for {SubscriptionId} at commit {Commit}", subscriptionDefinition.SubscriptionId, state.CommitPosition);
                            lastPersistedSequence = state.CommitPosition ?? 0L;
                            processedCount = 0;
                        }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to persist checkpoint for {SubscriptionId}", subscriptionDefinition.SubscriptionId);
                }
            }
        }
        finally
        {
        }
    }

    private static SubscriptionEvent MapEvent(SubscriptionDefinition subscription, ResolvedEvent resolvedEvent)
    {
        var originalEvent = resolvedEvent.OriginalEvent;
        return new SubscriptionEvent(
            originalEvent.EventId.ToString(),
            subscription.SubscriptionId,
            subscription.SecondaryIndexName,
            resolvedEvent.OriginalStreamId,
            originalEvent.EventType,
            originalEvent.Data.ToArray(),
            originalEvent.ContentType,
            ReadMetadata(originalEvent.Metadata),
            new DateTimeOffset(DateTime.SpecifyKind(originalEvent.Created, DateTimeKind.Utc)),
            resolvedEvent.OriginalPosition?.CommitPosition is ulong commitPos ? (long?)commitPos : null);
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
