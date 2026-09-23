using System.Net;
using System.Text.Json;
using SubscriptionService.Domain;

namespace SubscriptionService.Application;

public interface ICatchupServiceClient
{
    Task<CatchupApiResponse<IReadOnlyCollection<EndpointDefinition>>> GetEndpointsAsync(CancellationToken cancellationToken = default);
    Task<CatchupApiResponse<EndpointDefinition>> GetEndpointAsync(int endpointId, CancellationToken cancellationToken = default);
    Task<CatchupApiResponse<EndpointDefinition>> CreateEndpointAsync(EndpointDefinition endpoint, CancellationToken cancellationToken = default);
    Task<CatchupApiResponse<EndpointDefinition>> UpdateEndpointAsync(int endpointId, EndpointDefinition endpoint, CancellationToken cancellationToken = default);
    Task<CatchupApiResponse> DeleteEndpointAsync(int endpointId, CancellationToken cancellationToken = default);

    Task<CatchupApiResponse<IReadOnlyCollection<SubscriptionDefinition>>> GetSubscriptionConfigurationsAsync(CancellationToken cancellationToken = default);
    Task<CatchupApiResponse<SubscriptionDefinition>> GetSubscriptionConfigurationAsync(string subscriptionId, CancellationToken cancellationToken = default);
    Task<CatchupApiResponse<IReadOnlyCollection<DailyCommitPositionRecord>>> GetDailyPositionsAsync(string subscriptionId, string? secondaryIndexName = null, DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default);
    Task<CatchupApiResponse<SubscriptionDefinition>> CreateSubscriptionConfigurationAsync(SubscriptionConfigurationRequest request, CancellationToken cancellationToken = default);
    Task<CatchupApiResponse<SubscriptionDefinition>> UpdateSubscriptionConfigurationAsync(string subscriptionId, SubscriptionDefinition subscription, CancellationToken cancellationToken = default);
    Task<CatchupApiResponse> DeleteSubscriptionConfigurationAsync(string subscriptionId, CancellationToken cancellationToken = default);
    Task<CatchupApiResponse> StartSubscriptionAsync(string subscriptionId, CancellationToken cancellationToken = default);
    Task<CatchupApiResponse> StopSubscriptionAsync(string subscriptionId, CancellationToken cancellationToken = default);
    Task<CatchupApiResponse> EnableSubscriptionAsync(string subscriptionId, CancellationToken cancellationToken = default);
    Task<CatchupApiResponse> DisableSubscriptionAsync(string subscriptionId, CancellationToken cancellationToken = default);

    Task<CatchupApiResponse<IReadOnlyCollection<SubscriptionStatus>>> GetSubscriptionStatusesAsync(CancellationToken cancellationToken = default);
    Task<CatchupApiResponse<SubscriptionStatus>> GetSubscriptionStatusAsync(string subscriptionId, CancellationToken cancellationToken = default);
    Task<CatchupApiResponse<ReplayOperationResult>> StartReplayAsync(string subscriptionId, CancellationToken cancellationToken = default);
    Task<CatchupApiResponse<ReplayStatus>> GetReplayStatusAsync(string subscriptionId, CancellationToken cancellationToken = default);

    Task<CatchupApiResponse<string>> GetIndexesAsync(CancellationToken cancellationToken = default);
    Task<CatchupApiResponse<string>> GetIndexAsync(string indexName, CancellationToken cancellationToken = default);
    Task<CatchupApiResponse<string>> CreateIndexAsync(string indexName, JsonElement payload, CancellationToken cancellationToken = default);
    Task<CatchupApiResponse<string>> CreateIndexAsync(string indexName, string payload, CancellationToken cancellationToken = default);
    Task<CatchupApiResponse> DeleteIndexAsync(string indexName, CancellationToken cancellationToken = default);
}

public record CatchupApiResponse(HttpStatusCode StatusCode, string? Content, string? ContentType)
{
    public bool IsSuccessStatusCode => (int)StatusCode is >= 200 and < 300;
}

public sealed record CatchupApiResponse<T>(HttpStatusCode StatusCode, T? Value, string? Content, string? ContentType)
    : CatchupApiResponse(StatusCode, Content, ContentType);
