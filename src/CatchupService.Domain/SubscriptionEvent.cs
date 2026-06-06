namespace CatchupService.Domain;

public sealed record SubscriptionEvent(
    string EventId,
    string SubscriptionId,
    string SecondaryIndexName,
    string StreamName,
    string EventType,
    byte[] Payload,
    string ContentType,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset OccurredAt,
    long? CommitPosition)
{
    public static SubscriptionEvent Create(
        string eventId,
        string subscriptionId,
        string secondaryIndexName,
        string streamName,
        string eventType,
        byte[] payload,
        string contentType,
        IReadOnlyDictionary<string, string>? metadata = null,
    DateTimeOffset? occurredAt = null,
        long? commitPosition = null) =>
        new(
            eventId,
            subscriptionId,
            secondaryIndexName,
            streamName,
            eventType,
            payload,
            contentType,
            metadata ?? new Dictionary<string, string>(),
            occurredAt ?? DateTimeOffset.UtcNow,
            commitPosition);
}
