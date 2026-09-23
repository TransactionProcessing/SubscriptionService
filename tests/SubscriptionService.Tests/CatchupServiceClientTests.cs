using System.Net;
using SubscriptionService.Domain;
using SubscriptionService.Infrastructure;

namespace SubscriptionService.Tests;

public sealed class CatchupServiceClientTests
{
    [Fact]
    public async Task Create_endpoint_posts_json_to_endpoint_route()
    {
        var handler = new RecordingHandler(HttpStatusCode.Created, "{\"endpointId\":7,\"name\":\"Orders\",\"url\":\"https://consumer.test/events\"}");
        using var httpClient = CreateHttpClient(handler);
        var client = new CatchupServiceClient(httpClient);

        var response = await client.CreateEndpointAsync(new EndpointDefinition(7, "Orders", "https://consumer.test/events"));

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/endpoints", handler.RequestUri!.AbsolutePath);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(7, response.Value!.EndpointId);
        Assert.Contains("\"name\":\"Orders\"", handler.Body);
    }

    [Fact]
    public async Task Daily_positions_escapes_subscription_and_adds_optional_query_parameters()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "[]");
        using var httpClient = CreateHttpClient(handler);
        var client = new CatchupServiceClient(httpClient);

        await client.GetDailyPositionsAsync(
            "orders/uk",
            "index name",
            new DateTime(2026, 9, 1),
            new DateTime(2026, 9, 7));

        Assert.Equal("/subscriptions/config/orders%2Fuk/daily-positions", handler.RequestUri!.AbsolutePath);
        Assert.Equal("secondaryIndexName=index%20name&from=2026-09-01&to=2026-09-07", handler.RequestUri.Query.TrimStart('?'));
    }

    [Fact]
    public async Task Error_response_preserves_status_and_body_without_throwing()
    {
        var handler = new RecordingHandler(HttpStatusCode.Conflict, "{\"title\":\"already exists\"}");
        using var httpClient = CreateHttpClient(handler);
        var client = new CatchupServiceClient(httpClient);

        var response = await client.CreateEndpointAsync(new EndpointDefinition(7, "Orders", "https://consumer.test/events"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Null(response.Value);
        Assert.Equal("{\"title\":\"already exists\"}", response.Content);
    }

    private static HttpClient CreateHttpClient(RecordingHandler handler) => new(handler)
    {
        BaseAddress = new Uri("http://localhost:8080")
    };

    private sealed class RecordingHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.Method = request.Method;
            this.RequestUri = request.RequestUri;
            this.Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, System.Text.Encoding.UTF8, "application/json")
            };
        }
    }
}
