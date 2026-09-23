using SubscriptionService.Application;
using SubscriptionService.Domain;

namespace SubscriptionService.Worker;

public sealed class StreamEventCountUpdater
{
    private const int PageSize = 1000;

    private readonly IIndexEventReader _indexEventReader;
    private readonly ISubscriptionConfigurationStore _subscriptionConfigurationStore;
    private readonly IStreamEventCountStore _streamEventCountStore;
    private readonly ILogger<StreamEventCountUpdater> _logger;

    public StreamEventCountUpdater(
        IIndexEventReader indexEventReader,
        ISubscriptionConfigurationStore subscriptionConfigurationStore,
        IStreamEventCountStore streamEventCountStore,
        ILogger<StreamEventCountUpdater> logger)
    {
        this._indexEventReader = indexEventReader;
        this._subscriptionConfigurationStore = subscriptionConfigurationStore;
        this._streamEventCountStore = streamEventCountStore;
        this._logger = logger;
    }

    public async Task UpdateCountsAsync(CancellationToken cancellationToken)
    {
        var subscriptions = await this._subscriptionConfigurationStore.GetSubscriptionsAsync(cancellationToken);
        var groups = subscriptions
            .Where(x => x.Enabled)
            .Where(x => !string.IsNullOrWhiteSpace(x.SecondaryIndexName))
            .GroupBy(x => x.SecondaryIndexName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var group in groups)
        {
            var secondaryIndexName = group.Key;
            var subscriptionIds = group.Select(x => x.SubscriptionId).ToArray();
            var pageState = await this._streamEventCountStore.GetStateAsync(subscriptionIds[0], cancellationToken);
            var totalCount = pageState?.TotalCount ?? 0;
            long? checkpoint = pageState?.LastScannedCommitPosition;

            this._logger.LogInformation(
                "Starting count scan for index {SecondaryIndexName} across {SubscriptionCount} subscriptions ({Subscriptions}) from checkpoint {Checkpoint} with total {TotalCount}",
                secondaryIndexName,
                subscriptionIds.Length,
                string.Join(", ", subscriptionIds),
                checkpoint,
                totalCount);

            await this.UpdateIndexAsync(secondaryIndexName, subscriptionIds, totalCount, checkpoint, cancellationToken);
        }
    }

    private async Task UpdateIndexAsync(
        string secondaryIndexName,
        string[] subscriptionIds,
        long totalCount,
        long? checkpoint,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var previousCheckpoint = checkpoint;
            var page = await this._indexEventReader.ReadNextPageAsync(secondaryIndexName, checkpoint, PageSize, cancellationToken);

            var processedCount = 0;
            long? lastProcessedCommitPosition = checkpoint;

            foreach (var commitPosition in page.CommitPositions)
            {
                if (checkpoint.HasValue && commitPosition == checkpoint.Value)
                {
                    continue;
                }

                totalCount++;
                processedCount++;
                lastProcessedCommitPosition = commitPosition;
            }

            var resumeCommitPosition = page.ResumeCommitPosition;
            if (resumeCommitPosition is null)
            {
                resumeCommitPosition = lastProcessedCommitPosition;
            }
            else if (lastProcessedCommitPosition is long lastProcessed && resumeCommitPosition < lastProcessed)
            {
                resumeCommitPosition = lastProcessed;
            }
            if (resumeCommitPosition is null)
            {
                if (processedCount == 0)
                {
                    break;
                }

                this._logger.LogWarning(
                    "Unable to determine the next checkpoint for {SecondaryIndexName} after processing {ProcessedCount} events",
                    secondaryIndexName,
                    processedCount);
                break;
            }

            checkpoint = resumeCommitPosition;

            if (processedCount > 0 || checkpoint != previousCheckpoint)
            {
                foreach (var subscriptionId in subscriptionIds)
                {
                    await this._streamEventCountStore.UpsertAsync(
                        subscriptionId,
                        secondaryIndexName,
                        totalCount,
                        checkpoint,
                        DateTimeOffset.UtcNow,
                        cancellationToken);
                }

                this._logger.LogInformation(
                    "Updated count for {SecondaryIndexName} = {TotalCount} (checkpoint {Checkpoint}) across {SubscriptionCount} subscriptions",
                    secondaryIndexName,
                    totalCount,
                    checkpoint,
                    subscriptionIds.Length);
            }

            if (page.CommitPositions.Count < PageSize)
            {
                break;
            }

            if (checkpoint == previousCheckpoint && processedCount == 0)
            {
                this._logger.LogWarning(
                    "Stream event count scan for {SecondaryIndexName} did not advance from checkpoint {Checkpoint}",
                    secondaryIndexName,
                    checkpoint);
                break;
            }
        }
    }
}
