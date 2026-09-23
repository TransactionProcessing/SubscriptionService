namespace SubscriptionService.Application;

public sealed record SubscriptionDeliverySnapshot(
    string EventId,
    string StreamName,
    string EventType,
    DateTimeOffset DeliveredAt,
    string Status,
    string? FailureReason);
