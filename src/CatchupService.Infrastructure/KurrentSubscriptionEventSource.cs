using System.Text.Json;
using CatchupService.Domain;
using KurrentDB.Client;

namespace CatchupService.Infrastructure;

public sealed class KurrentSubscriptionEventSource(KurrentDBClient client) : ISubscriptionEventSource
{
    public async Task SubscribeAsync(
        SubscriptionDefinition subscription,
        long afterSequenceNumber,
        Func<SubscriptionEvent, CancellationToken, Task<bool>> eventAppeared,
        CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellationRegistration = cancellationToken.Register(() => completed.TrySetResult());

        StreamSubscription? streamSubscription = null;
        try
        {
            streamSubscription = await client.SubscribeToStreamAsync(
                subscription.SecondaryIndexName,
                FromStream.After(StreamPosition.FromInt64(afterSequenceNumber)),
                async (_, resolvedEvent, eventCancellationToken) =>
                {
                    try
                    {
                        var delivered = await eventAppeared(
                            MapEvent(subscription, resolvedEvent),
                            eventCancellationToken);

                        if (!delivered)
                        {
                            completed.TrySetResult();
                            linkedCts.Cancel();
                        }
                    }
                    catch (Exception ex)
                    {
                        completed.TrySetException(ex);
                        linkedCts.Cancel();
                        throw;
                    }
                },
                subscriptionDropped: (_, reason, exception) =>
                {
                    if (reason == SubscriptionDroppedReason.Disposed)
                    {
                        completed.TrySetResult();
                        return;
                    }

                    completed.TrySetException(
                        exception ?? new InvalidOperationException($"Subscription {subscription.SubscriptionId} was dropped with reason {reason}."));
                    linkedCts.Cancel();
                },
                cancellationToken: linkedCts.Token);

            await completed.Task.ConfigureAwait(false);
        }
        finally
        {
            streamSubscription?.Dispose();
        }
    }

    private static SubscriptionEvent MapEvent(SubscriptionDefinition subscription, ResolvedEvent resolvedEvent)
    {
        var originalEvent = resolvedEvent.OriginalEvent;
        return new SubscriptionEvent(
            originalEvent.EventId.ToString(),
            subscription.SubscriptionId,
            subscription.SecondaryIndexName,
            originalEvent.EventNumber.ToInt64(),
            resolvedEvent.OriginalStreamId,
            originalEvent.EventType,
            originalEvent.Data.ToArray(),
            originalEvent.ContentType,
            ReadMetadata(originalEvent.Metadata),
            new DateTimeOffset(DateTime.SpecifyKind(originalEvent.Created, DateTimeKind.Utc)));
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
