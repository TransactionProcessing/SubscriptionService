using CatchupService.Application;
using CatchupService.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CatchupService.Worker;

public sealed class SubscriptionHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<SubscriptionWorkerOptions> workerOptions,
    ILogger<SubscriptionHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriptions = workerOptions.Value.Subscriptions;
        if (subscriptions.Count == 0)
        {
            logger.LogWarning("No subscriptions configured. Worker will remain idle.");
            await Task.Delay(Timeout.Infinite, stoppingToken);
            return;
        }

        var running = subscriptions.Select(subscription => RunSubscriptionAsync(subscription, stoppingToken)).ToArray();
        await Task.WhenAll(running);
    }

    private async Task RunSubscriptionAsync(SubscriptionDefinition subscription, CancellationToken stoppingToken)
    {
        logger.LogInformation("Starting subscription worker for {Subscription}", subscription.Name);
        using var scope = scopeFactory.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<SubscriptionProcessor>();
        await processor.RunAsync(subscription, stoppingToken);
    }
}
