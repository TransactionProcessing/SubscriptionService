using System.Text;
using System.Text.Json;
using SubscriptionService.Application;

namespace SubscriptionService.Infrastructure;

public sealed class EventStoreIndexClient(HttpClient httpClient) : IEventStoreIndexClient
{
    public Task<EventStoreIndexApiResponse> GetAllAsync(CancellationToken cancellationToken = default) =>
        this.SendAsync(HttpMethod.Get, "v2/indexes", null, cancellationToken);

    public Task<EventStoreIndexApiResponse> GetAsync(string indexName, CancellationToken cancellationToken = default) =>
        this.SendAsync(HttpMethod.Get, BuildPath(indexName), null, cancellationToken);

    public Task<EventStoreIndexApiResponse> CreateAsync(string indexName, JsonElement payload, CancellationToken cancellationToken = default) =>
        this.SendAsync(HttpMethod.Post, BuildPath(indexName), payload.GetRawText(), cancellationToken);

    public Task<EventStoreIndexApiResponse> CreateAsync(string indexName, string payload, CancellationToken cancellationToken = default) =>
        this.SendAsync(HttpMethod.Post, BuildPath(indexName), payload, cancellationToken);

    public async Task<EventStoreIndexApiResponse> UpdateAsync(string indexName, string payload, CancellationToken cancellationToken = default)
    {
        var deleteResponse = await this.DeleteAsync(indexName, cancellationToken);
        if ((int)deleteResponse.StatusCode is < 200 or >= 300)
        {
            return deleteResponse;
        }

        return await this.CreateAsync(indexName, payload, cancellationToken);
    }

    public Task<EventStoreIndexApiResponse> DeleteAsync(string indexName, CancellationToken cancellationToken = default) =>
        this.SendAsync(HttpMethod.Delete, BuildPath(indexName), null, cancellationToken);

    private async Task<EventStoreIndexApiResponse> SendAsync(
        HttpMethod method,
        string relativePath,
        string? content,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativePath);

        if (content is not null)
        {
            request.Content = new StringContent(content, Encoding.UTF8, "application/json");
        }

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var responseContent = response.Content is null
            ? null
            : await response.Content.ReadAsStringAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(responseContent))
        {
            responseContent = null;
        }

        var contentType = response.Content?.Headers.ContentType?.ToString();
        return new EventStoreIndexApiResponse(response.StatusCode, responseContent, contentType);
    }

    private static string BuildPath(string indexName) =>
        $"v2/indexes/{Uri.EscapeDataString(indexName)}";
}
