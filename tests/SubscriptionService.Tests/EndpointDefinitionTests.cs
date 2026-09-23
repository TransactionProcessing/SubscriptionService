using SubscriptionService.Domain;

namespace SubscriptionService.Tests;

public sealed class EndpointDefinitionTests
{
    [Fact]
    public void EndpointDefinition_exposes_complete_delivery_target()
    {
        var endpoint = new EndpointDefinition(
            7,
            "Orders API",
            "https://example.test/orders",
            new AuthenticationConfiguration("Bearer", new Dictionary<string, string> { ["token"] = "secret" }));

        Assert.Equal(7, endpoint.EndpointId);
        Assert.Equal("Orders API", endpoint.Name);
        Assert.Equal(new Uri("https://example.test/orders"), endpoint.Uri);
        Assert.Equal("Bearer", endpoint.Authentication?.Scheme);
        Assert.Equal("secret", endpoint.Authentication?.Parameters["token"]);
    }
}
