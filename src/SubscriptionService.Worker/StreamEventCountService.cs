using System.Collections.Concurrent;
using KurrentDB.Client;
using SubscriptionService.Application;
using SubscriptionService.Domain;

namespace SubscriptionService.Worker;

public sealed class StreamEventCountService(
    KurrentDBClient client,
    ISubscriptionConfigurationStore configurationStore,
    IStreamEventCountStore streamEventCountStore,
    CountConnectionRegistry diagnosticsRegistry,
    CountWorkerStatusRegistry workerStatusRegistry,
    WorkerOptions options,
    ILogger<StreamEventCountService> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<string, RunningCountSubscription> _runningSubscriptions = new(StringComparer.OrdinalIgnoreCase);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            logger.LogInformation("Independent count worker starting");

            while (!stoppingToken.IsCancellationRequested)
            {
                var subscriptions = await configurationStore.GetSubscriptionsAsync(stoppingToken);
                var countSubscriptions = subscriptions
                    .Where(x => x.Enabled && x.OperationalState == SubscriptionOperationalState.Healthy && !string.IsNullOrWhiteSpace(x.SecondaryIndexName))
                    .OrderBy(x => x.SubscriptionId)
                    .ToArray();

                workerStatusRegistry.SetConfigured(countSubscriptions.Length);

                var desired = countSubscriptions.ToDictionary(x => x.SubscriptionId, StringComparer.OrdinalIgnoreCase);

                foreach (var (subscriptionId, current) in desired)
                {
                    if (this._runningSubscriptions.TryGetValue(subscriptionId, out var running))
                    {
                        if (running.Definition != current)
                        {
                            await this.StopCountSubscriptionAsync(subscriptionId, running);
                            await this.StartCountSubscriptionAsync(current, stoppingToken);
                        }
                    }
                    else
                    {
                        await this.StartCountSubscriptionAsync(current, stoppingToken);
                    }
                }

                foreach (var (subscriptionId, running) in this._runningSubscriptions.ToArray())
                {
                    if (!desired.ContainsKey(subscriptionId))
                    {
                        await this.StopCountSubscriptionAsync(subscriptionId, running);
                    }
                }

                await Task.Delay(options.ConfigurationPollInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            workerStatusRegistry.MarkFailed(ex.Message);
            logger.LogError(ex, "Count worker failed");
        }
    }

    private Task StartCountSubscriptionAsync(SubscriptionDefinition subscription, CancellationToken stoppingToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var task = Task.Run(() => this.RunCountSubscriptionAsync(subscription, cts.Token), cts.Token);
        this._runningSubscriptions[subscription.SubscriptionId] = new RunningCountSubscription(subscription, cts, task);

        _ = task.ContinueWith(t =>
        {
            this._runningSubscriptions.TryRemove(subscription.SubscriptionId, out _);
        }, TaskScheduler.Default);

        return Task.CompletedTask;
    }

    private async Task StopCountSubscriptionAsync(string subscriptionId, RunningCountSubscription running)
    {
        running.CancellationTokenSource.Cancel();
        this._runningSubscriptions.TryRemove(subscriptionId, out _);

        try
        {
            await running.Task;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RunCountSubscriptionAsync(SubscriptionDefinition subscription, CancellationToken stoppingToken)
    {
        var indexName = subscription.SecondaryIndexName.Trim();
        var persistedState = await streamEventCountStore.GetStateAsync(subscription.SubscriptionId, stoppingToken);
        var totalCount = persistedState?.TotalCount ?? 0L;
        long? lastCommitPosition = persistedState?.LastScannedCommitPosition;
        var eventsSincePersistence = 0;
        var lastPersistedAt = persistedState?.UpdatedAt ?? DateTimeOffset.UtcNow;
        var stateNeedsPersistence = persistedState is null;
        var caughtUp = false;

        async Task PersistStateAsync(CancellationToken cancellationToken)
        {
            var persistedAt = DateTimeOffset.UtcNow;
            await streamEventCountStore.UpsertAsync(
                subscription.SubscriptionId,
                indexName,
                totalCount,
                lastCommitPosition,
                persistedAt,
                cancellationToken);
            eventsSincePersistence = 0;
            lastPersistedAt = persistedAt;
            stateNeedsPersistence = false;
        }

        diagnosticsRegistry.Upsert(
            new CountConnectionSnapshot(
                subscription.SubscriptionId,
                indexName,
                totalCount,
                lastCommitPosition,
                caughtUp,
                "Starting",
                null,
                null,
                null,
                DateTimeOffset.UtcNow,
                null));

        workerStatusRegistry.MarkAttempted();

        logger.LogInformation(
            "Count subscription starting for {SubscriptionId} on index {SecondaryIndexName}",
            subscription.SubscriptionId,
            indexName);

        var filterOptions = new SubscriptionFilterOptions(StreamFilter.Prefix(indexName));

        try
        {
            var startPosition = lastCommitPosition is long startCommitPosition && startCommitPosition > 0
                ? FromAll.After(new Position((ulong)startCommitPosition, (ulong)startCommitPosition))
                : FromAll.Start;

            await using var subscriptionReader = client.SubscribeToAll(startPosition, filterOptions: filterOptions);
            workerStatusRegistry.MarkStarted();

            await foreach (var message in subscriptionReader.Messages.WithCancellation(stoppingToken))
            {
                if (message is StreamMessage.CaughtUp)
                {
                    caughtUp = true;
                    await PersistStateAsync(stoppingToken);
                    diagnosticsRegistry.Upsert(
                        new CountConnectionSnapshot(
                            subscription.SubscriptionId,
                            indexName,
                            totalCount,
                            lastCommitPosition,
                            true,
                            "CaughtUp",
                            null,
                            null,
                            null,
                            DateTimeOffset.UtcNow,
                            null));

                    continue;
                }

                if (message is not StreamMessage.Event(var resolvedEvent))
                {
                    continue;
                }

                totalCount++;
                eventsSincePersistence++;
                stateNeedsPersistence = true;
                lastCommitPosition = resolvedEvent.OriginalPosition?.CommitPosition is ulong commitPosition ? (long?)commitPosition : null;

                if (StreamEventCountPersistencePolicy.ShouldPersist(
                        eventsSincePersistence,
                        lastPersistedAt,
                        DateTimeOffset.UtcNow))
                {
                    await PersistStateAsync(stoppingToken);
                }

                diagnosticsRegistry.Upsert(
                    new CountConnectionSnapshot(
                        subscription.SubscriptionId,
                        indexName,
                        totalCount,
                        lastCommitPosition,
                        caughtUp,
                        caughtUp ? "Live" : "Counting",
                        resolvedEvent.OriginalEvent.EventId.ToString(),
                        resolvedEvent.OriginalStreamId,
                        resolvedEvent.OriginalEvent.EventType,
                        DateTimeOffset.UtcNow,
                        null));

            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            diagnosticsRegistry.Upsert(
                new CountConnectionSnapshot(
                    subscription.SubscriptionId,
                    indexName,
                    totalCount,
                    lastCommitPosition,
                    caughtUp,
                    "Stopped",
                    null,
                    null,
                    null,
                    DateTimeOffset.UtcNow,
                    null));
        }
        catch (Exception ex)
        {
            workerStatusRegistry.MarkFailed(ex.Message);
            logger.LogError(ex, "Count subscription failed for {SubscriptionId}", subscription.SubscriptionId);
            diagnosticsRegistry.Upsert(
                new CountConnectionSnapshot(
                    subscription.SubscriptionId,
                    indexName,
                    totalCount,
                    lastCommitPosition,
                    caughtUp,
                    "Faulted",
                    null,
                    null,
                    null,
                    DateTimeOffset.UtcNow,
                    ex.Message));
        }
        finally
        {
            if (stateNeedsPersistence)
            {
                try
                {
                    await PersistStateAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to persist final count checkpoint for {SubscriptionId}", subscription.SubscriptionId);
                }
            }

            logger.LogInformation(
                "Count subscription ended for {SubscriptionId} with total {TotalCount}",
                subscription.SubscriptionId,
                totalCount);
        }
    }

    private sealed record RunningCountSubscription(
        SubscriptionDefinition Definition,
        CancellationTokenSource CancellationTokenSource,
        Task Task);
}
