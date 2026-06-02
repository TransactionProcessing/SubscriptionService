namespace CatchupService.Core;

public sealed record SubscriptionDefinition(
    string Name,
    Uri Endpoint,
    int QueueCapacity = 256,
    int MaxDeliveryAttempts = 3,
    TimeSpan DeliveryTimeout = default,
    TimeSpan RetryDelay = default);
