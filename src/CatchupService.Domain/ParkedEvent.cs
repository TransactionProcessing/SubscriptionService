namespace CatchupService.Domain;

public sealed record ParkedEvent(
    Guid ParkedEventId,
    string SubscriptionId,
    string EventId,
    long SequenceNumber,
    string StreamName,
    string EventType,
    string FailureReason,
    byte[] Payload,
    string ContentType,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset ParkedAt,
    int AttemptCount)
{
    public static ParkedEvent FromEvent(SubscriptionEvent @event, string failureReason, int attemptCount) =>
        new(
            Guid.NewGuid(),
            @event.SubscriptionId,
            @event.EventId,
            @event.SequenceNumber,
            @event.StreamName,
            @event.EventType,
            failureReason,
            @event.Payload,
            @event.ContentType,
            @event.Metadata,
            DateTimeOffset.UtcNow,
            attemptCount);
}
