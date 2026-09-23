using System.Net;
using SubscriptionService.Infrastructure;

namespace SubscriptionService.Tests;

public sealed class EventStoreIndexClientTests
{
    [Fact]
    public async Task GetAllAsync_SendsGetToIndexesEndpoint()
    {
        var handler = new InspectingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json")
        });

        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:2113/")
        };

        var sut = new EventStoreIndexClient(client);

        var response = await sut.GetAllAsync();

        Assert.Single(handler.Methods);
        Assert.Equal(HttpMethod.Get, handler.Methods[0]);
        Assert.Single(handler.RequestUris);
        Assert.Equal(new Uri("http://localhost:2113/v2/indexes"), handler.RequestUris[0]);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[]", response.Content);
        Assert.StartsWith("application/json", response.ContentType);
    }

    [Fact]
    public async Task CreateAsync_SendsRawBodyToIndexEndpoint()
    {
        var rawBody = "{ not json and that is fine }";
        var handler = new InspectingHandler(new HttpResponseMessage(HttpStatusCode.Created));

        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:2113/")
        };

        var sut = new EventStoreIndexClient(client);

        var response = await sut.CreateAsync("events-by-organisation", rawBody);

        Assert.Single(handler.Methods);
        Assert.Equal(HttpMethod.Post, handler.Methods[0]);
        Assert.Single(handler.RequestUris);
        Assert.Equal(new Uri("http://localhost:2113/v2/indexes/events-by-organisation"), handler.RequestUris[0]);
        Assert.Single(handler.Bodies);
        Assert.Equal(rawBody, handler.Bodies[0]);
        Assert.Single(handler.ContentTypes);
        Assert.Equal("application/json", handler.ContentTypes[0]);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAsync_SendsDeleteToIndexEndpoint()
    {
        var handler = new InspectingHandler(new HttpResponseMessage(HttpStatusCode.NoContent));

        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:2113/")
        };

        var sut = new EventStoreIndexClient(client);

        var response = await sut.DeleteAsync("events-by-organisation");

        Assert.Single(handler.Methods);
        Assert.Equal(HttpMethod.Delete, handler.Methods[0]);
        Assert.Single(handler.RequestUris);
        Assert.Equal(new Uri("http://localhost:2113/v2/indexes/events-by-organisation"), handler.RequestUris[0]);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(response.Content);
    }

    [Fact]
    public async Task UpdateAsync_SendsDeleteThenCreateToIndexEndpoint()
    {
        var handler = new InspectingHandler(
            new HttpResponseMessage(HttpStatusCode.NoContent),
            new HttpResponseMessage(HttpStatusCode.Created));

        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:2113/")
        };

        var sut = new EventStoreIndexClient(client);
        var rawBody = "{ pretend update payload }";

        var response = await sut.UpdateAsync("events-by-organisation", rawBody);

        Assert.Collection(
            handler.Methods,
            method => Assert.Equal(HttpMethod.Delete, method),
            method => Assert.Equal(HttpMethod.Post, method));

        Assert.Collection(
            handler.RequestUris,
            uri => Assert.Equal(new Uri("http://localhost:2113/v2/indexes/events-by-organisation"), uri),
            uri => Assert.Equal(new Uri("http://localhost:2113/v2/indexes/events-by-organisation"), uri));

        Assert.Collection(
            handler.Bodies,
            body => Assert.Null(body),
            body => Assert.Equal(rawBody, body));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private sealed class InspectingHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        public List<HttpMethod> Methods { get; } = [];

        public List<Uri> RequestUris { get; } = [];

        public List<string?> Bodies { get; } = [];

        public List<string?> ContentTypes { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.Methods.Add(request.Method);
            this.RequestUris.Add(request.RequestUri!);
            this.ContentTypes.Add(request.Content?.Headers.ContentType?.MediaType);

            if (request.Content is not null)
            {
                this.Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }
            else
            {
                this.Bodies.Add(null);
            }

            return responses[Math.Min(this.Methods.Count - 1, responses.Length - 1)];
        }
    }
}
