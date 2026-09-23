using SubscriptionService.Application;
using SubscriptionService.Domain;

namespace SubscriptionService.Worker;

public static class SubscriptionConfigurationEndpoints
{
    public static WebApplication MapSubscriptionConfigurationEndpoints(this WebApplication app)
    {
        app.MapGet("/subscriptions/config", async (ISubscriptionConfigurationStore store, CancellationToken cancellationToken) =>
            await store.GetSubscriptionsAsync(cancellationToken));

        app.MapGet("/subscriptions/config/{subscriptionId}", async (string subscriptionId, ISubscriptionConfigurationStore store, CancellationToken cancellationToken) =>
        {
            var subs = await store.GetSubscriptionsAsync(cancellationToken);
            var s = subs.FirstOrDefault(x => string.Equals(x.SubscriptionId, subscriptionId, StringComparison.OrdinalIgnoreCase));
            return s is null ? Results.NotFound() : TypedResults.Ok(s);
        });

        app.MapGet("/subscriptions/config/{subscriptionId}/daily-positions", async (string subscriptionId, string? secondaryIndexName, DateTime? from, DateTime? to, IDailyCommitPositionStore store, CancellationToken cancellationToken) =>
        {
            var fromDate = from ?? DateTime.UtcNow.AddDays(-30).Date;
            var toDate = to ?? DateTime.UtcNow.Date;
            var rows = await store.GetPositionsAsync(subscriptionId, secondaryIndexName, fromDate, toDate, cancellationToken);
            return TypedResults.Ok(rows);
        });

        app.MapPost("/subscriptions/config", CreateAsync);

        app.MapPut("/subscriptions/config/{subscriptionId}", async (string subscriptionId, SubscriptionDefinition subscription, ISubscriptionConfigurationStore store, CancellationToken cancellationToken) =>
        {
            if (!string.Equals(subscriptionId, subscription.SubscriptionId, StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest("SubscriptionId in path and body must match");
            }

            await store.UpsertAsync(subscription, cancellationToken);
            return Results.Accepted($"/subscriptions/config/{subscriptionId}", subscription);
        });

        app.MapDelete("/subscriptions/config/{subscriptionId}", async (string subscriptionId, ISubscriptionConfigurationStore store, ICheckpointStore checkpointStore, CancellationToken cancellationToken) =>
        {
            await store.RemoveAsync(subscriptionId, cancellationToken);
            await checkpointStore.RemoveAsync(subscriptionId, cancellationToken);
            return Results.NoContent();
        });

        app.MapPost("/subscriptions/config/{subscriptionId}/start", async (string subscriptionId, ISubscriptionConfigurationStore store, CancellationToken cancellationToken) =>
        {
            await store.SetOperationalStateAsync(subscriptionId, SubscriptionOperationalState.Healthy, null, cancellationToken);
            return Results.Accepted($"/subscriptions/config/{subscriptionId}");
        });

        app.MapPost("/subscriptions/config/{subscriptionId}/stop", async (string subscriptionId, ISubscriptionConfigurationStore store, CancellationToken cancellationToken) =>
        {
            await store.SetOperationalStateAsync(subscriptionId, SubscriptionOperationalState.Stopped, null, cancellationToken);
            return Results.Accepted($"/subscriptions/config/{subscriptionId}");
        });

        app.MapPost("/subscriptions/config/{subscriptionId}/enable", async (string subscriptionId, ISubscriptionConfigurationStore store, CancellationToken cancellationToken) =>
        {
            await store.SetOperationalStateAsync(subscriptionId, SubscriptionOperationalState.Healthy, null, cancellationToken);
            return Results.Accepted($"/subscriptions/config/{subscriptionId}");
        });

        app.MapPost("/subscriptions/config/{subscriptionId}/disable", async (string subscriptionId, ISubscriptionConfigurationStore store, CancellationToken cancellationToken) =>
        {
            await store.SetOperationalStateAsync(subscriptionId, SubscriptionOperationalState.Stopped, null, cancellationToken);
            return Results.Accepted($"/subscriptions/config/{subscriptionId}");
        });

        return app;
    }

    public static async Task<IResult> CreateAsync(
        SubscriptionConfigurationRequest request,
        ISubscriptionConfigurationStore store,
        CancellationToken cancellationToken = default)
    {
        var mapping = SubscriptionConfigurationRequestMapper.Map(request);
        if (mapping.Errors.Count != 0)
            return Results.ValidationProblem(mapping.Errors);

        var subscription = mapping.Subscription!;
        var existing = await store.GetSubscriptionsAsync(cancellationToken);
        if (existing.Any(x => string.Equals(x.SubscriptionId, subscription.SubscriptionId, StringComparison.OrdinalIgnoreCase)))
        {
            return Results.Conflict(new
            {
                title = "Subscription configuration already exists.",
                detail = $"A subscription configuration with id '{subscription.SubscriptionId}' already exists.",
                subscriptionId = subscription.SubscriptionId
            });
        }

        await store.UpsertAsync(subscription, cancellationToken);
        return Results.Created($"/subscriptions/config/{subscription.SubscriptionId}", subscription);
    }
}
