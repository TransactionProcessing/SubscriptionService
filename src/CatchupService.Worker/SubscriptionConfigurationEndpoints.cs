using CatchupService.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace CatchupService.Worker;

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

        app.MapPost("/subscriptions/config", async (SubscriptionDefinition subscription, ISubscriptionConfigurationStore store, CancellationToken cancellationToken) =>
        {
            await store.UpsertAsync(subscription, cancellationToken);
            return Results.Created($"/subscriptions/config/{subscription.SubscriptionId}", subscription);
        });

        app.MapPut("/subscriptions/config/{subscriptionId}", async (string subscriptionId, SubscriptionDefinition subscription, ISubscriptionConfigurationStore store, CancellationToken cancellationToken) =>
        {
            if (!string.Equals(subscriptionId, subscription.SubscriptionId, StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest("SubscriptionId in path and body must match");
            }

            await store.UpsertAsync(subscription, cancellationToken);
            return Results.Accepted($"/subscriptions/config/{subscriptionId}", subscription);
        });

        app.MapDelete("/subscriptions/config/{subscriptionId}", async (string subscriptionId, ISubscriptionConfigurationStore store, CancellationToken cancellationToken) =>
        {
            await store.RemoveAsync(subscriptionId, cancellationToken);
            return Results.NoContent();
        });

        app.MapPost("/subscriptions/config/{subscriptionId}/enable", async (string subscriptionId, ISubscriptionConfigurationStore store, CancellationToken cancellationToken) =>
        {
            var subs = await store.GetSubscriptionsAsync(cancellationToken);
            var s = subs.FirstOrDefault(x => string.Equals(x.SubscriptionId, subscriptionId, StringComparison.OrdinalIgnoreCase));
            if (s is null) return Results.NotFound();
            var updated = s with { Enabled = true };
            await store.UpsertAsync(updated, cancellationToken);
            return Results.Accepted($"/subscriptions/config/{subscriptionId}", updated);
        });

        app.MapPost("/subscriptions/config/{subscriptionId}/disable", async (string subscriptionId, ISubscriptionConfigurationStore store, CancellationToken cancellationToken) =>
        {
            var subs = await store.GetSubscriptionsAsync(cancellationToken);
            var s = subs.FirstOrDefault(x => string.Equals(x.SubscriptionId, subscriptionId, StringComparison.OrdinalIgnoreCase));
            if (s is null) return Results.NotFound();
            var updated = s with { Enabled = false };
            await store.UpsertAsync(updated, cancellationToken);
            return Results.Accepted($"/subscriptions/config/{subscriptionId}", updated);
        });

        return app;
    }
}
