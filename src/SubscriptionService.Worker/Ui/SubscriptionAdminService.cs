using System.Text;
using SubscriptionService.Application;
using SubscriptionService.Domain;

namespace SubscriptionService.Worker.Ui;

public sealed class SubscriptionAdminService(
    ISubscriptionConfigurationStore configurationStore,
    ICheckpointStore checkpointStore,
    ISubscriptionStatusService statusService,
    ISubscriptionReplayService replayService,
    DailyCommitPositionBackfillService dailyCommitPositionBackfillService,
    IParkedEventStore parkedEventStore,
    IDailyCommitPositionStore dailyCommitPositionStore,
    IEventStoreIndexClient indexClient,
    IEventStoreBuiltInIndexClient builtInIndexClient,
    ISubscriptionEventSource eventSource,
    WorkerRuntimeRegistry runtimeRegistry,
    IEndpointStore endpointStore,
    ILogger<SubscriptionAdminService> logger)
{
    public async Task<IReadOnlyList<EndpointViewModel>> GetEndpointOptionsAsync(CancellationToken cancellationToken = default) =>
        (await endpointStore.GetAllAsync(cancellationToken)).Select(x => new EndpointViewModel(x.EndpointId, x.Name, x.Url, x.Authentication?.Scheme)).ToArray();

    public Task SaveEndpointAsync(EndpointDefinition endpoint, CancellationToken cancellationToken = default) => endpointStore.UpsertAsync(endpoint, cancellationToken);

    public Task<bool> DeleteEndpointAsync(int endpointId, CancellationToken cancellationToken = default) => endpointStore.RemoveAsync(endpointId, cancellationToken);

    public async Task<EndpointEditorModel?> GetEndpointEditorModelAsync(int? endpointId, CancellationToken cancellationToken = default)
    {
        if (endpointId is null or 0) return new EndpointEditorModel();
        var endpoint = await endpointStore.GetAsync(endpointId.Value, cancellationToken);
        return endpoint is null ? null : new EndpointEditorModel
        {
            EndpointId = endpoint.EndpointId,
            Name = endpoint.Name,
            Url = endpoint.Url,
            AuthenticationScheme = endpoint.Authentication?.Scheme,
            AuthenticationParametersJson = endpoint.Authentication is null ? "{}" : System.Text.Json.JsonSerializer.Serialize(endpoint.Authentication.Parameters)
        };
    }

    public async Task<DashboardViewModel> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var subscriptionsTask = configurationStore.GetSubscriptionsAsync(cancellationToken);
        var statusesTask = statusService.GetAllAsync(cancellationToken);
        await Task.WhenAll(subscriptionsTask, statusesTask);

        var subscriptions = subscriptionsTask.Result;
        var statuses = statusesTask.Result.ToDictionary(x => x.SubscriptionId, StringComparer.OrdinalIgnoreCase);

        var cards = subscriptions
            .OrderBy(x => x.SubscriptionId)
            .Select(subscription =>
            {
                statuses.TryGetValue(subscription.SubscriptionId, out var status);
                var runtimeProcessedCount = runtimeRegistry.Get(subscription.SubscriptionId)?.GetProcessedCount();
                var processedCount = runtimeProcessedCount ?? status?.ProcessedCount ?? 0;
                return new DashboardSubscriptionCard(
                    subscription.SubscriptionId,
                    subscription.SecondaryIndexName,
                    subscription.EndpointUrl,
                    subscription.Tag,
                    subscription.EnableEventLogging,
                    subscription.Enabled,
                    status?.IsRunning ?? false,
                    status?.Health ?? "Unknown",
                    status?.OperationalState ?? subscription.OperationalState,
                    status?.OperationalReason ?? subscription.OperationalReason,
                    status?.HasActiveReplaySession ?? false,
                    status?.ParkedEventCount ?? 0,
                    processedCount,
                    status?.CommitPosition,
                    BuildProgressDisplay(processedCount, status?.TotalEventsInSubscription ?? 0),
                    status?.LatestParkedAt,
                    status?.LatestParkedFailureReason);
            })
            .ToArray();

        return new DashboardViewModel(
            cards.Length,
            cards.Count(x => x.Enabled),
            cards.Count(x => x.IsRunning),
            cards.Count(x => x.HasActiveReplaySession),
            cards.Sum(x => x.ParkedEventCount),
            cards);
    }

    public async Task<SubscriptionDefinition?> GetSubscriptionAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        var subscriptions = await configurationStore.GetSubscriptionsAsync(cancellationToken);
        return subscriptions.FirstOrDefault(x => string.Equals(x.SubscriptionId, subscriptionId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<SubscriptionEditorModel?> GetEditorModelAsync(string? subscriptionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subscriptionId))
        {
            return SubscriptionEditorModel.CreateDefault();
        }

        var subscription = await this.GetSubscriptionAsync(subscriptionId, cancellationToken);
        return subscription is null
            ? null
            : SubscriptionEditorModel.FromDomain(subscription);
    }

    public async Task<IReadOnlyList<EventStoreIndexListItemViewModel>> GetIndexOptionsAsync(CancellationToken cancellationToken = default)
    {
        var userIndexesTask = indexClient.GetAllAsync(cancellationToken);
        var categoriesTask = builtInIndexClient.GetCategoriesAsync(cancellationToken);
        var eventTypesTask = builtInIndexClient.GetEventTypesAsync(cancellationToken);
        await Task.WhenAll(userIndexesTask, categoriesTask, eventTypesTask);

        var builtInIndexes = categoriesTask.Result
            .Select(category => EventStoreIndexPresentation.CreateBuiltInIndex($"$idx-ce-{category}", "Category"))
            .Concat(eventTypesTask.Result.Select(eventType =>
                EventStoreIndexPresentation.CreateBuiltInIndex($"$idx-et-{eventType}", "Event type")));

        return EventStoreIndexPresentation.CombineIndexLists(
            EventStoreIndexPresentation.ParseIndexList(userIndexesTask.Result.Content),
            builtInIndexes);
    }

    public async Task<IReadOnlyList<SubscriptionEvent>> PreviewEventsAsync(
        string secondaryIndexName,
        int maxEvents,
        CancellationToken cancellationToken = default)
    {
        var indexName = secondaryIndexName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(indexName))
        {
            throw new InvalidOperationException("Please provide a secondary index name before previewing events.");
        }

        if (maxEvents <= 0)
        {
            return Array.Empty<SubscriptionEvent>();
        }

        var previewSubscription = CreatePreviewSubscription(indexName);
        return await eventSource.PeekAsync(previewSubscription, maxEvents, cancellationToken);
    }

    public async Task SaveAsync(SubscriptionDefinition subscription, CancellationToken cancellationToken = default)
    {
        await configurationStore.UpsertAsync(subscription, cancellationToken);
    }

    public async Task<bool> DeleteAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        var subscription = await this.GetSubscriptionAsync(subscriptionId, cancellationToken);
        if (subscription is null)
        {
            return false;
        }

        await configurationStore.RemoveAsync(subscriptionId, cancellationToken);
        await checkpointStore.RemoveAsync(subscriptionId, cancellationToken);
        return true;
    }

    private static string? BuildProgressDisplay(long processedCount, long totalEvents)
    {
        if (totalEvents <= 0)
        {
            return null;
        }

        var percent = Math.Min(100.0, (double)processedCount / totalEvents * 100.0);
        return $"{percent:0.##}% ({processedCount}/{totalEvents})";
    }

    public async Task<bool> SetEnabledAsync(string subscriptionId, bool enabled, CancellationToken cancellationToken = default)
    {
        return enabled
            ? await this.StartAsync(subscriptionId, cancellationToken)
            : await this.StopAsync(subscriptionId, cancellationToken);
    }

    public async Task<bool> StartAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        var subscription = await this.GetSubscriptionAsync(subscriptionId, cancellationToken);
        if (subscription is null)
        {
            return false;
        }

        var updated = subscription with
        {
            Enabled = true,
            OperationalState = SubscriptionOperationalState.Healthy,
            OperationalReason = null
        };

        await configurationStore.UpsertAsync(updated, cancellationToken);
        return true;
    }

    public async Task<bool> StopAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        var subscription = await this.GetSubscriptionAsync(subscriptionId, cancellationToken);
        if (subscription is null)
        {
            return false;
        }

        var updated = subscription with
        {
            Enabled = false,
            OperationalState = SubscriptionOperationalState.Stopped,
            OperationalReason = null
        };

        await configurationStore.UpsertAsync(updated, cancellationToken);
        return true;
    }

    public Task<ReplayOperationResult> ReplayAsync(string subscriptionId, CancellationToken cancellationToken = default) =>
        replayService.ReplayAsync(subscriptionId, cancellationToken);

    public Task<ReplayOperationResult> ReplayFromCheckpointAsync(string subscriptionId, long commitPosition, CancellationToken cancellationToken = default) =>
        replayService.ReplayFromCheckpointAsync(subscriptionId, commitPosition, cancellationToken);

    public async Task<DailyCommitPositionBackfillResult> BackfillDailyCommitsAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default)
    {
        var subscription = await this.GetSubscriptionAsync(subscriptionId, cancellationToken);
        if (subscription is null)
        {
            return new(subscriptionId, false, "Subscription not found.");
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await dailyCommitPositionBackfillService.BackfillAsync(subscriptionId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Daily commit backfill failed for subscription {SubscriptionId}", subscriptionId);
            }
        }, CancellationToken.None);

        return new(subscriptionId, true, "Daily commit backfill started from the beginning of the index.");
    }

    public Task<SubscriptionDeliverySnapshot?> GetLatestDeliverySnapshotAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        var runtime = runtimeRegistry.Get(subscriptionId);
        return Task.FromResult(runtime?.GetLastDeliverySnapshot());
    }

    public async Task<SubscriptionDetailViewModel?> GetDetailAsync(
        string subscriptionId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        var subscription = await this.GetSubscriptionAsync(subscriptionId, cancellationToken);
        if (subscription is null)
        {
            return null;
        }

        if (fromDate > toDate)
        {
            (fromDate, toDate) = (toDate, fromDate);
        }

        fromDate = fromDate.Date;
        toDate = toDate.Date;

        var statusTask = statusService.GetAsync(subscriptionId, cancellationToken);
        var replayTask = replayService.GetStatusAsync(subscriptionId, cancellationToken);
        var checkpointTask = checkpointStore.GetCheckpointAsync(subscriptionId, cancellationToken);
        var parkedTask = parkedEventStore.GetParkedEventsAsync(subscriptionId, cancellationToken);
        var positionsTask = dailyCommitPositionStore.GetPositionsAsync(
            subscriptionId,
            string.IsNullOrWhiteSpace(subscription.SecondaryIndexName) ? null : subscription.SecondaryIndexName,
            fromDate,
            toDate,
            cancellationToken);

        await Task.WhenAll(statusTask, replayTask, checkpointTask, parkedTask, positionsTask);

        var status = statusTask.Result;
        var runtimeProcessedCount = runtimeRegistry.Get(subscriptionId)?.GetProcessedCount();
        if (status is not null && runtimeProcessedCount.HasValue)
        {
            status = status with
            {
                ProcessedCount = runtimeProcessedCount.Value,
                ProgressPercent = status.TotalEventsInSubscription <= 0
                    ? 0
                    : Math.Min(100.0, (double)runtimeProcessedCount.Value / status.TotalEventsInSubscription * 100.0),
                ProgressDisplay = BuildProgressDisplay(runtimeProcessedCount.Value, status.TotalEventsInSubscription)
            };
        }

        return new SubscriptionDetailViewModel(
            subscription,
            status,
            replayTask.Result,
            checkpointTask.Result,
            positionsTask.Result,
            parkedTask.Result,
            fromDate,
            toDate);
    }

    public static string FormatPreview(byte[] payload, int maxLength = 200)
    {
        var text = Encoding.UTF8.GetString(payload);
        text = text.Replace("\r", " ").Replace("\n", " ").Trim();
        if (text.Length <= maxLength)
        {
            return text;
        }

        return text[..Math.Max(0, maxLength - 1)] + "…";
    }

    private static SubscriptionDefinition CreatePreviewSubscription(string secondaryIndexName) =>
        new(
            $"preview-{Guid.NewGuid():N}",
            secondaryIndexName,
            "https://preview.local/",
            "preview",
            TimeoutSettings.Default,
            RetrySettings.Default,
            CheckpointSettings.Default);
}
