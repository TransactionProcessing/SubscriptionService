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
    WorkerRuntimeRegistry runtimeRegistry,
    RunningSubscriptionRegistry runningSubscriptionRegistry,
    ILoggerFactory loggerFactory,
    WorkerOptions options,
    ILogger<Worker> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<string, RunningSubscription> _runningSubscriptions = new(StringComparer.OrdinalIgnoreCase);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Catchup subscription service starting");

        while (!stoppingToken.IsCancellationRequested)
        {
            var subscriptions = (await configurationStore.GetSubscriptionsAsync(stoppingToken))
                .Where(x => x.Enabled)
                .ToArray();

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

            await Task.Delay(options.ConfigurationPollInterval, stoppingToken);
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
            configurationStore,
            loggerFactory.CreateLogger<SubscriptionRuntime>());
        // Load persisted checkpoint into runtime before starting poller so processed counts and commit position
        // are available on startup and the subscription won't remain paused due to stale in-memory state.
        var chk = checkpointStore.GetCheckpointAsync(subscription.SubscriptionId).GetAwaiter().GetResult();
        runtime.InitializeAsync(chk).GetAwaiter().GetResult();
        runtimeRegistry.Register(subscription.SubscriptionId, runtime);
        var poller = new SubscriptionPoller(
            eventSource,
            checkpointStore,
            runtime,
            options.SubscriptionResubscribeDelay,
            loggerFactory.CreateLogger<SubscriptionPoller>());

        var task = Task.Run(() => poller.RunAsync(subscription, cts.Token), cts.Token);
        var running = new RunningSubscription(subscription, cts, task);
        _runningSubscriptions[subscription.SubscriptionId] = running;
        runningSubscriptionRegistry.MarkRunning(subscription.SubscriptionId);

        // When the poller task completes (e.g., subscription paused or finished), ensure we clean up
        // the running registry and remove the running subscription so status reflects the actual state
        // (parked events should not cause a "Paused" state by themselves).
        _ = task.ContinueWith(t =>
        {
            _runningSubscriptions.TryRemove(subscription.SubscriptionId, out _);
            runtimeRegistry.Unregister(subscription.SubscriptionId);
            runningSubscriptionRegistry.MarkStopped(subscription.SubscriptionId);
        }, TaskScheduler.Default);
        return Task.CompletedTask;
    }

    private async Task StopRunningSubscriptionAsync(string subscriptionId, RunningSubscription running)
    {
        running.CancellationTokenSource.Cancel();
        _runningSubscriptions.TryRemove(subscriptionId, out _);
        runtimeRegistry.Unregister(subscriptionId);
        runningSubscriptionRegistry.MarkStopped(subscriptionId);

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
