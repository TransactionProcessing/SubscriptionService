using SubscriptionService.Domain;

namespace SubscriptionService.Worker;

public static class EndpointEndpoints
{
    public static WebApplication MapEndpointEndpoints(this WebApplication app)
    {
        app.MapGet("/endpoints", (IEndpointStore store, CancellationToken cancellationToken) => store.GetAllAsync(cancellationToken));
        app.MapGet("/endpoints/{endpointId}", async (int endpointId, IEndpointStore store, CancellationToken cancellationToken) =>
        {
            var endpoint = await store.GetAsync(endpointId, cancellationToken);
            return endpoint is null ? Results.NotFound() : Results.Ok(endpoint);
        });
        app.MapPost("/endpoints", async (EndpointDefinition endpoint, IEndpointStore store, CancellationToken cancellationToken) =>
        {
            await store.UpsertAsync(endpoint, cancellationToken);
            return Results.Created($"/endpoints/{endpoint.EndpointId}", endpoint);
        });
        app.MapPut("/endpoints/{endpointId}", async (int endpointId, EndpointDefinition endpoint, IEndpointStore store, CancellationToken cancellationToken) =>
        {
            if (endpointId != endpoint.EndpointId) return Results.BadRequest("EndpointId in path and body must match");
            await store.UpsertAsync(endpoint, cancellationToken);
            return Results.Accepted($"/endpoints/{endpointId}", endpoint);
        });
        app.MapDelete("/endpoints/{endpointId}", async (int endpointId, IEndpointStore store, CancellationToken cancellationToken) =>
        {
            try { return await store.RemoveAsync(endpointId, cancellationToken) ? Results.NoContent() : Results.NotFound(); }
            catch (InvalidOperationException ex) { return Results.Conflict(ex.Message); }
        });
        return app;
    }
}
