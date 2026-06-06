using CatchupService.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace CatchupService.Worker;

public static class SubscriptionStatusEndpoints
{
    public static WebApplication MapSubscriptionStatusEndpoints(this WebApplication app)
    {
        app.MapGet("/subscriptions/status", async (ISubscriptionStatusService statusService, CancellationToken cancellationToken) =>
            await statusService.GetAllAsync(cancellationToken));

        app.MapGet("/subscriptions/{subscriptionId}/status", async (string subscriptionId, ISubscriptionStatusService statusService, CancellationToken cancellationToken) =>
        {
            var status = await statusService.GetAsync(subscriptionId, cancellationToken);
            return status is null ? Results.NotFound() : TypedResults.Ok(status);
        });

        app.MapPost("/subscriptions/{subscriptionId}/replay", async (string subscriptionId, ISubscriptionReplayService replayService, CancellationToken cancellationToken) =>
        {
            var result = await replayService.ReplayAsync(subscriptionId, cancellationToken);
            return result.Started
                ? Results.Accepted($"/subscriptions/{subscriptionId}/replay", result)
                : Results.NotFound(result);
        });

        app.MapGet("/subscriptions/{subscriptionId}/replay", async (string subscriptionId, ISubscriptionReplayService replayService, CancellationToken cancellationToken) =>
        {
            var status = await replayService.GetStatusAsync(subscriptionId, cancellationToken);
            return status is null ? Results.NotFound() : TypedResults.Ok(status);
        });

        return app;
    }
}