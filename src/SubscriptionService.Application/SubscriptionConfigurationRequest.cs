using SubscriptionService.Domain;

namespace SubscriptionService.Application;

public sealed class SubscriptionConfigurationRequest
{
    public string? SubscriptionId { get; init; }
    public string? SecondaryIndexName { get; init; }
    public string? EndpointUrl { get; init; }
    public string? Tag { get; init; }
    public int TimeoutSeconds { get; init; } = 30;
    public int RetryMaxAttempts { get; init; } = 3;
    public int RetryDelaySeconds { get; init; } = 1;
    public int CheckpointBatchSize { get; init; } = 100;
    public bool ContinueOnParked { get; init; }
    public AuthenticationConfiguration? Authentication { get; init; }
    public bool SoftDeleteParked { get; init; } = true;
    public bool EnableEventLogging { get; init; }
}
