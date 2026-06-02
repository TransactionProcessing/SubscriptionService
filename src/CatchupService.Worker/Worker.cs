using CatchupService.Application;
using CatchupService.Domain;
using CatchupService.Infrastructure;
using System.Collections.Concurrent;

namespace CatchupService.Worker;

public sealed class Worker(
    ISubscriptionConfigurationStore configurationStore,
    ISubscriptionEventSource eventSource,
    ICheckpointStore checkpointStore,
    IParkedEventStore parkedEventStore,
    IReplaySessionStore replaySessionStore,
    IEventDeliveryClient deliveryClient,
    ILoggerFactory loggerFactory,
    ILogger<Worker> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<string, RunningSubscription> _runningSubscriptions = new(StringComparer.OrdinalIgnoreCase);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Catchup subscription service starting");

        while (!stoppingToken.IsCancellationRequested)
        {
            var subscriptions = await configurationStore.GetSubscriptionsAsync(stoppingToken);
            var desired = subscriptions.ToDictionary(x => x.SubscriptionId, StringComparer.OrdinalIgnoreCase);

            foreach (var (subscriptionId, current) in desired)
            {
                if (_runningSubscriptions.TryGetValue(subscriptionId, out var running))
                {
                    if (running.Definition != current)
                    {
                        await StopRunningSubscriptionAsync(subscriptionId, running);
                        await StartRunningSubscriptionAsync(current, stoppingToken);
                    }
                }
                else
                {
                    await StartRunningSubscriptionAsync(current, stoppingToken);
                }
            }

            foreach (var (subscriptionId, running) in _runningSubscriptions.ToArray())
            {
                if (!desired.ContainsKey(subscriptionId))
                {
                    await StopRunningSubscriptionAsync(subscriptionId, running);
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    private Task StartRunningSubscriptionAsync(SubscriptionDefinition subscription, CancellationToken stoppingToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var runtime = new SubscriptionRuntime(
            deliveryClient,
            checkpointStore,
            parkedEventStore,
            replaySessionStore,
            loggerFactory.CreateLogger<SubscriptionRuntime>());
        var poller = new SubscriptionPoller(
            eventSource,
            checkpointStore,
            runtime,
            loggerFactory.CreateLogger<SubscriptionPoller>());

        var running = new RunningSubscription(subscription, cts, Task.Run(() => poller.RunAsync(subscription, cts.Token), cts.Token));
        _runningSubscriptions[subscription.SubscriptionId] = running;
        return Task.CompletedTask;
    }

    private async Task StopRunningSubscriptionAsync(string subscriptionId, RunningSubscription running)
    {
        running.CancellationTokenSource.Cancel();
        _runningSubscriptions.TryRemove(subscriptionId, out _);

        try
        {
            await running.Task;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed record RunningSubscription(
        SubscriptionDefinition Definition,
        CancellationTokenSource CancellationTokenSource,
        Task Task);
}
