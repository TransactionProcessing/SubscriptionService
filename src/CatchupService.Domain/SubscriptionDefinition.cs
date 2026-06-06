namespace CatchupService.Domain;

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
    public bool Enabled { get; init; } = true;
    public bool SoftDeleteParked { get; init; } = true;

    public Uri Endpoint => new(EndpointUrl, UriKind.Absolute);
}
