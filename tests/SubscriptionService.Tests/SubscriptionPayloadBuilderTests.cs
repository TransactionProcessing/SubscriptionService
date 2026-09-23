using System.Text;
using System.Text.Json;
using SubscriptionService.Infrastructure;

namespace SubscriptionService.Tests;

public sealed class SubscriptionPayloadBuilderTests
{
    [Fact]
    public void Build_AddsTheEventIdToAJsonObjectPayload()
    {
        var payload = SubscriptionPayloadBuilder.Build(
            Encoding.UTF8.GetBytes("""{"orderId":123,"status":"created"}"""),
            "evt-123");

        using var document = JsonDocument.Parse(payload);

        Assert.Equal("evt-123", document.RootElement.GetProperty("EventId").GetString());
        Assert.Equal(123, document.RootElement.GetProperty("orderId").GetInt32());
        Assert.Equal("created", document.RootElement.GetProperty("status").GetString());
    }
}
