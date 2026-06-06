namespace CatchupService.Domain;

public sealed record ParkedEvent(
    Guid ParkedEventId,
    string SubscriptionId,
    string EventId,
    string StreamName,
    string EventType,
    DateTimeOffset OccurredAt,
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
            @event.StreamName,
            @event.EventType,
            @event.OccurredAt,
            failureReason,
            @event.Payload,
            @event.ContentType,
            @event.Metadata,
            DateTimeOffset.UtcNow,
            attemptCount);
}
