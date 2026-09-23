namespace SubscriptionService.Domain;

public sealed record EndpointDefinition(
    int EndpointId,
    string Name,
    string Url,
    AuthenticationConfiguration? Authentication = null)
{
    public Uri Uri => new(Url, UriKind.Absolute);
}
