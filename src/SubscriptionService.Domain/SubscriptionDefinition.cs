namespace SubscriptionService.Domain;

public sealed record SubscriptionDefinition(
    string SubscriptionId,
    string SecondaryIndexName,
    string EndpointUrl,
    string Tag,
    TimeoutSettings Timeout,
    RetrySettings Retry,
    CheckpointSettings Checkpoint,
    bool ContinueOnParked = false,
    AuthenticationConfiguration? Authentication = null)
{
    public int? EndpointId { get; init; }
    public bool Enabled { get; init; } = true;
    public SubscriptionOperationalState OperationalState { get; init; } = SubscriptionOperationalState.Healthy;
    public string? OperationalReason { get; init; }
    public bool SoftDeleteParked { get; init; } = true;
    public bool EnableEventLogging { get; init; }

    public Uri Endpoint => new(EndpointUrl, UriKind.Absolute);
}
