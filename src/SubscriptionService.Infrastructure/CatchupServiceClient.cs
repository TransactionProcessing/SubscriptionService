using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SubscriptionService.Application;
using SubscriptionService.Domain;

namespace SubscriptionService.Infrastructure;

public sealed class CatchupServiceClient(HttpClient httpClient) : ICatchupServiceClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<CatchupApiResponse<IReadOnlyCollection<EndpointDefinition>>> GetEndpointsAsync(CancellationToken cancellationToken = default) => this.SendJsonAsync<IReadOnlyCollection<EndpointDefinition>>(HttpMethod.Get, "endpoints", null, cancellationToken);

    public Task<CatchupApiResponse<EndpointDefinition>> GetEndpointAsync(int endpointId, CancellationToken cancellationToken = default) => this.SendJsonAsync<EndpointDefinition>(HttpMethod.Get, $"endpoints/{endpointId}", null, cancellationToken);

    public Task<CatchupApiResponse<EndpointDefinition>> CreateEndpointAsync(EndpointDefinition endpoint, CancellationToken cancellationToken = default) => this.SendJsonAsync<EndpointDefinition>(HttpMethod.Post, "endpoints", endpoint, cancellationToken);

    public Task<CatchupApiResponse<EndpointDefinition>> UpdateEndpointAsync(int endpointId, EndpointDefinition endpoint, CancellationToken cancellationToken = default) => this.SendJsonAsync<EndpointDefinition>(HttpMethod.Put, $"endpoints/{endpointId}", endpoint, cancellationToken);

    public Task<CatchupApiResponse> DeleteEndpointAsync(int endpointId, CancellationToken cancellationToken = default) =>
        this.SendAsync(HttpMethod.Delete, $"endpoints/{endpointId}", null, cancellationToken);

    public Task<CatchupApiResponse<IReadOnlyCollection<SubscriptionDefinition>>> GetSubscriptionConfigurationsAsync(CancellationToken cancellationToken = default) => this.SendJsonAsync<IReadOnlyCollection<SubscriptionDefinition>>(HttpMethod.Get, "subscriptions/config", null, cancellationToken);

    public Task<CatchupApiResponse<SubscriptionDefinition>> GetSubscriptionConfigurationAsync(string subscriptionId, CancellationToken cancellationToken = default) => this.SendJsonAsync<SubscriptionDefinition>(HttpMethod.Get, $"subscriptions/config/{Escape(subscriptionId)}", null, cancellationToken);

    public Task<CatchupApiResponse<IReadOnlyCollection<DailyCommitPositionRecord>>> GetDailyPositionsAsync(
        string subscriptionId,
        string? secondaryIndexName = null,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        AddQuery(query, "secondaryIndexName", secondaryIndexName);
        AddQuery(query, "from", from?.ToString("yyyy-MM-dd"));
        AddQuery(query, "to", to?.ToString("yyyy-MM-dd"));
        var path = $"subscriptions/config/{Escape(subscriptionId)}/daily-positions";
        return this.SendJsonAsync<IReadOnlyCollection<DailyCommitPositionRecord>>(HttpMethod.Get, AddQueryString(path, query), null, cancellationToken);
    }

    public Task<CatchupApiResponse<SubscriptionDefinition>> CreateSubscriptionConfigurationAsync(SubscriptionConfigurationRequest request, CancellationToken cancellationToken = default) => this.SendJsonAsync<SubscriptionDefinition>(HttpMethod.Post, "subscriptions/config", request, cancellationToken);

    public Task<CatchupApiResponse<SubscriptionDefinition>> UpdateSubscriptionConfigurationAsync(string subscriptionId, SubscriptionDefinition subscription, CancellationToken cancellationToken = default) => this.SendJsonAsync<SubscriptionDefinition>(HttpMethod.Put, $"subscriptions/config/{Escape(subscriptionId)}", subscription, cancellationToken);

    public Task<CatchupApiResponse> DeleteSubscriptionConfigurationAsync(string subscriptionId, CancellationToken cancellationToken = default) =>
        this.SendAsync(HttpMethod.Delete, $"subscriptions/config/{Escape(subscriptionId)}", null, cancellationToken);

    public Task<CatchupApiResponse> StartSubscriptionAsync(string subscriptionId, CancellationToken cancellationToken = default) =>
        this.SendAsync(HttpMethod.Post, $"subscriptions/config/{Escape(subscriptionId)}/start", null, cancellationToken);

    public Task<CatchupApiResponse> StopSubscriptionAsync(string subscriptionId, CancellationToken cancellationToken = default) =>
        this.SendAsync(HttpMethod.Post, $"subscriptions/config/{Escape(subscriptionId)}/stop", null, cancellationToken);

    public Task<CatchupApiResponse> EnableSubscriptionAsync(string subscriptionId, CancellationToken cancellationToken = default) =>
        this.SendAsync(HttpMethod.Post, $"subscriptions/config/{Escape(subscriptionId)}/enable", null, cancellationToken);

    public Task<CatchupApiResponse> DisableSubscriptionAsync(string subscriptionId, CancellationToken cancellationToken = default) =>
        this.SendAsync(HttpMethod.Post, $"subscriptions/config/{Escape(subscriptionId)}/disable", null, cancellationToken);

    public Task<CatchupApiResponse<IReadOnlyCollection<SubscriptionStatus>>> GetSubscriptionStatusesAsync(CancellationToken cancellationToken = default) => this.SendJsonAsync<IReadOnlyCollection<SubscriptionStatus>>(HttpMethod.Get, "subscriptions/status", null, cancellationToken);

    public Task<CatchupApiResponse<SubscriptionStatus>> GetSubscriptionStatusAsync(string subscriptionId, CancellationToken cancellationToken = default) => this.SendJsonAsync<SubscriptionStatus>(HttpMethod.Get, $"subscriptions/{Escape(subscriptionId)}/status", null, cancellationToken);

    public Task<CatchupApiResponse<ReplayOperationResult>> StartReplayAsync(string subscriptionId, CancellationToken cancellationToken = default) => this.SendJsonAsync<ReplayOperationResult>(HttpMethod.Post, $"subscriptions/{Escape(subscriptionId)}/replay", null, cancellationToken);

    public Task<CatchupApiResponse<ReplayStatus>> GetReplayStatusAsync(string subscriptionId, CancellationToken cancellationToken = default) => this.SendJsonAsync<ReplayStatus>(HttpMethod.Get, $"subscriptions/{Escape(subscriptionId)}/replay", null, cancellationToken);

    public Task<CatchupApiResponse<string>> GetIndexesAsync(CancellationToken cancellationToken = default) =>
        this.SendRawAsync(HttpMethod.Get, "indexes", null, cancellationToken);

    public Task<CatchupApiResponse<string>> GetIndexAsync(string indexName, CancellationToken cancellationToken = default) =>
        this.SendRawAsync(HttpMethod.Get, $"indexes/{Escape(indexName)}", null, cancellationToken);

    public Task<CatchupApiResponse<string>> CreateIndexAsync(string indexName, JsonElement payload, CancellationToken cancellationToken = default) =>
        this.SendRawAsync(HttpMethod.Post, $"indexes/{Escape(indexName)}", payload.GetRawText(), cancellationToken);

    public Task<CatchupApiResponse<string>> CreateIndexAsync(string indexName, string payload, CancellationToken cancellationToken = default) =>
        this.SendRawAsync(HttpMethod.Post, $"indexes/{Escape(indexName)}", payload, cancellationToken);

    public Task<CatchupApiResponse> DeleteIndexAsync(string indexName, CancellationToken cancellationToken = default) =>
        this.SendAsync(HttpMethod.Delete, $"indexes/{Escape(indexName)}", null, cancellationToken);

    private async Task<CatchupApiResponse<T>> SendJsonAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        var response = await this.SendAsync(method, path, body, cancellationToken);
        T? value = default;
        if (response.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(response.Content))
            value = JsonSerializer.Deserialize<T>(response.Content, JsonOptions);

        return new(response.StatusCode, value, response.Content, response.ContentType);
    }

    private async Task<CatchupApiResponse<string>> SendRawAsync(HttpMethod method, string path, string? body, CancellationToken cancellationToken)
    {
        var response = await this.SendAsync(method, path, body is null ? null : new StringContent(body, Encoding.UTF8, "application/json"), cancellationToken);
        return new(response.StatusCode, response.Content, response.Content, response.ContentType);
    }

    private async Task<CatchupApiResponse> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var content = body is null ? null : JsonContent.Create(body, options: JsonOptions);
        return await this.SendAsync(method, path, content, cancellationToken);
    }

    private async Task<CatchupApiResponse> SendAsync(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        return new(response.StatusCode, string.IsNullOrWhiteSpace(responseContent) ? null : responseContent, response.Content.Headers.ContentType?.ToString());
    }

    private static string Escape(string value) => Uri.EscapeDataString(value);

    private static void AddQuery(ICollection<string> query, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            query.Add($"{Escape(name)}={Escape(value)}");
    }

    private static string AddQueryString(string path, IReadOnlyCollection<string> query) =>
        query.Count == 0 ? path : $"{path}?{string.Join('&', query)}";
}
