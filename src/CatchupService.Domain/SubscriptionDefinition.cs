namespace CatchupService.Domain;

public sealed record SubscriptionDefinition(
    string SubscriptionId,
    string SecondaryIndexName,
    string EndpointUrl,
    string Tag,
    TimeoutSettings Timeout,
    RetrySettings Retry,
    CheckpointSettings Checkpoint,
    AuthenticationConfiguration? Authentication = null)
{
    public Uri Endpoint => new(EndpointUrl, UriKind.Absolute);
}
