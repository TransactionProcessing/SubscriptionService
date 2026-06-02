namespace CatchupService.Domain;

public sealed record SubscriptionEvent(
    string EventId,
    string SubscriptionId,
    string SecondaryIndexName,
    long SequenceNumber,
    string StreamName,
    string EventType,
    byte[] Payload,
    string ContentType,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset OccurredAt)
{
    public static SubscriptionEvent Create(
        string eventId,
        string subscriptionId,
        string secondaryIndexName,
        long sequenceNumber,
        string streamName,
        string eventType,
        byte[] payload,
        string contentType,
        IReadOnlyDictionary<string, string>? metadata = null,
        DateTimeOffset? occurredAt = null) =>
        new(
            eventId,
            subscriptionId,
            secondaryIndexName,
            sequenceNumber,
            streamName,
            eventType,
            payload,
            contentType,
            metadata ?? new Dictionary<string, string>(),
            occurredAt ?? DateTimeOffset.UtcNow);
}
