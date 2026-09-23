using System.Text.Json;
using SubscriptionService.Application;

namespace SubscriptionService.Worker;

public static class EventStoreIndexEndpoints
{
    public static WebApplication MapEventStoreIndexEndpoints(this WebApplication app)
    {
        app.MapGet("/indexes", async (IEventStoreIndexClient client, CancellationToken cancellationToken) =>
            ToResult(await client.GetAllAsync(cancellationToken)));

        app.MapGet("/indexes/{indexName}", async (string indexName, IEventStoreIndexClient client, CancellationToken cancellationToken) =>
            ToResult(await client.GetAsync(indexName, cancellationToken)));

        app.MapPost("/indexes/{indexName}", async (string indexName, JsonElement payload, IEventStoreIndexClient client, CancellationToken cancellationToken) =>
            ToResult(await client.CreateAsync(indexName, payload, cancellationToken)));

        app.MapDelete("/indexes/{indexName}", async (string indexName, IEventStoreIndexClient client, CancellationToken cancellationToken) =>
            ToResult(await client.DeleteAsync(indexName, cancellationToken)));

        return app;
    }

    private static IResult ToResult(EventStoreIndexApiResponse response)
    {
        if (!response.HasContent)
        {
            return Results.StatusCode((int)response.StatusCode);
        }

        return Results.Content(
            response.Content!,
            response.ContentType ?? "application/json",
            statusCode: (int)response.StatusCode);
    }
}
