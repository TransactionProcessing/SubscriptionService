using System.Collections.Concurrent;
using SubscriptionService.Application;
using SubscriptionService.Domain;

namespace SubscriptionService.Worker;

public sealed class Worker(
    ISubscriptionConfigurationStore configurationStore,
    ISubscriptionEventSource eventSource,
    ICheckpointStore checkpointStore,
    IParkedEventStore parkedEventStore,
    IReplaySessionStore replaySessionStore,
    ISubscriptionEventLogStore subscriptionEventLogStore,
    IEventDeliveryClient deliveryClient,
    WorkerRuntimeRegistry runtimeRegistry,
    RunningSubscriptionRegistry runningSubscriptionRegistry,
    ILoggerFactory loggerFactory,
    WorkerOptions options,
    ILogger<Worker> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<string, RunningSubscription> _runningSubscriptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly SubscriptionRestartTracker _restartTracker = new(options.SubscriptionResubscribeDelay);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Catchup subscription service starting");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var subscriptions = (await configurationStore.GetSubscriptionsAsync(stoppingToken))
                    .Where(x => x.Enabled && x.OperationalState == SubscriptionOperationalState.Healthy)
                    .ToArray();

                var desired = subscriptions.ToDictionary(x => x.SubscriptionId, StringComparer.OrdinalIgnoreCase);

                foreach (var (subscriptionId, current) in desired)
                {
                    if (this._runningSubscriptions.TryGetValue(subscriptionId, out var running))
                    {
                        if (running.Definition != current)
                        {
                            await this.StopRunningSubscriptionAsync(subscriptionId, running);
                            await this.StartRunningSubscriptionAsync(current, stoppingToken);
                        }
                    }
                    else
                    {
                        if (!this._restartTracker.IsReady(subscriptionId))
                        {
                            continue;
                        }

                        await this.StartRunningSubscriptionAsync(current, stoppingToken);
                    }
                }

                foreach (var (subscriptionId, running) in this._runningSubscriptions.ToArray())
                {
                    if (!desired.ContainsKey(subscriptionId))
                    {
                        await this.StopRunningSubscriptionAsync(subscriptionId, running);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Subscription configuration poll failed; retrying after {PollInterval}", options.ConfigurationPollInterval);
            }

            try
            {
                await Task.Delay(options.ConfigurationPollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
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
            loggerFactory.CreateLogger<SubscriptionRuntime>(),
            subscriptionEventLogStore);
        // Load persisted checkpoint into runtime before starting poller so processed counts and commit position
        // are available on startup and the subscription won't remain paused due to stale in-memory state.
        var chk = checkpointStore.GetCheckpointAsync(subscription.SubscriptionId).GetAwaiter().GetResult();
        runtime.InitializeAsync(chk).GetAwaiter().GetResult();
        var poller = new SubscriptionPoller(
            eventSource,
            checkpointStore,
            configurationStore,
            runtime,
            options.SubscriptionResubscribeDelay,
            loggerFactory.CreateLogger<SubscriptionPoller>());

        var task = Task.Run(() => poller.RunAsync(subscription, cts.Token), cts.Token);
        var running = new RunningSubscription(subscription, cts, task);
        this._runningSubscriptions[subscription.SubscriptionId] = running;
        runtimeRegistry.Register(subscription.SubscriptionId, runtime, cts);
        runningSubscriptionRegistry.MarkRunning(subscription.SubscriptionId);
        logger.LogInformation("Started subscription worker for {SubscriptionId}", subscription.SubscriptionId);
        this._restartTracker.Clear(subscription.SubscriptionId);

        // When the poller task completes (e.g., subscription paused or finished), ensure we clean up
        // the running registry and remove the running subscription so status reflects the actual state
        // (parked events should not cause a "Paused" state by themselves).
        _ = task.ContinueWith(t =>
        {
            if (t.IsFaulted && t.Exception is not null)
            {
                var exception = t.Exception.GetBaseException();
                runningSubscriptionRegistry.MarkFailed(subscription.SubscriptionId, exception);
                this._restartTracker.Schedule(subscription.SubscriptionId);
                logger.LogError(
                    exception,
                    "Subscription worker stopped unexpectedly for {SubscriptionId}; restarting after {Delay}",
                    subscription.SubscriptionId,
                    options.SubscriptionResubscribeDelay);
            }

            if (t is { IsCompletedSuccessfully: true } && !running.CancellationTokenSource.IsCancellationRequested)
            {
                const string reason = "Subscription loop completed unexpectedly";
                runningSubscriptionRegistry.MarkStopped(subscription.SubscriptionId, reason);
                this._restartTracker.Schedule(subscription.SubscriptionId);
                logger.LogWarning(
                    "Subscription worker completed unexpectedly for {SubscriptionId}; restarting after {Delay}",
                    subscription.SubscriptionId,
                    options.SubscriptionResubscribeDelay);
            }

            if (this._runningSubscriptions.TryGetValue(subscription.SubscriptionId, out var current)
                && ReferenceEquals(current, running))
            {
                this._runningSubscriptions.TryRemove(subscription.SubscriptionId, out _);
            }
            runtimeRegistry.Unregister(subscription.SubscriptionId);
            if (!t.IsFaulted)
            {
                if (running.CancellationTokenSource.IsCancellationRequested)
                {
                    runningSubscriptionRegistry.MarkStopped(subscription.SubscriptionId, "Subscription stopped by configuration or service cancellation");
                }
            }
        }, TaskScheduler.Default);
        return Task.CompletedTask;
    }

    private async Task StopRunningSubscriptionAsync(string subscriptionId, RunningSubscription running)
    {
        running.CancellationTokenSource.Cancel();
        this._runningSubscriptions.TryRemove(subscriptionId, out _);
        runtimeRegistry.Unregister(subscriptionId);
        runningSubscriptionRegistry.MarkStopped(subscriptionId, "Subscription stopped by configuration or service cancellation");

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
