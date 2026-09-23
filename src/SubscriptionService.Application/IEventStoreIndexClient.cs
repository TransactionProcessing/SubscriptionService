using System.Net;
using System.Text.Json;

namespace SubscriptionService.Application;

public interface IEventStoreIndexClient
{
    Task<EventStoreIndexApiResponse> GetAllAsync(CancellationToken cancellationToken = default);

    Task<EventStoreIndexApiResponse> GetAsync(string indexName, CancellationToken cancellationToken = default);

    Task<EventStoreIndexApiResponse> CreateAsync(string indexName, JsonElement payload, CancellationToken cancellationToken = default);

    Task<EventStoreIndexApiResponse> CreateAsync(string indexName, string payload, CancellationToken cancellationToken = default);

    Task<EventStoreIndexApiResponse> UpdateAsync(string indexName, string payload, CancellationToken cancellationToken = default);

    Task<EventStoreIndexApiResponse> DeleteAsync(string indexName, CancellationToken cancellationToken = default);
}

public sealed record EventStoreIndexApiResponse(HttpStatusCode StatusCode, string? Content, string? ContentType)
{
    public bool HasContent => !string.IsNullOrWhiteSpace(Content);
}
