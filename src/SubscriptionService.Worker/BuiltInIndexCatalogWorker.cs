using KurrentDB.Client;
using SubscriptionService.Domain;

namespace SubscriptionService.Worker;

public sealed class BuiltInIndexCatalogWorker(
    KurrentDBClient client,
    IBuiltInIndexCatalogStore catalogStore,
    IIndexScanStateStore scanStateStore,
    ILogger<BuiltInIndexCatalogWorker> logger) : BackgroundService
{
    private const string CheckpointName = "$built-in-index-catalog";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var checkpoint = await scanStateStore.GetLastScannedCommitPositionAsync(CheckpointName, stoppingToken);
            var start = checkpoint is long position
                ? FromAll.After(new Position((ulong)position, (ulong)position))
                : FromAll.Start;

            await using var subscription = client.SubscribeToAll(start);
            await foreach (var message in subscription.Messages.WithCancellation(stoppingToken))
            {
                if (message is StreamMessage.Event(var resolvedEvent))
                {
                    var eventType = resolvedEvent.OriginalEvent.EventType;
                    var stream = resolvedEvent.OriginalStreamId;
                    var commit = resolvedEvent.OriginalPosition?.CommitPosition;

                    if (!eventType.StartsWith('$') && !stream.StartsWith('$'))
                    {
                        var category = stream.Contains('-')
                            ? stream[..stream.IndexOf('-')]
                            : stream;
                        await catalogStore.UpsertAsync(
                            "$idx-ce-" + category,
                            "Category",
                            commit is ulong categoryCommit ? (long?)categoryCommit : null,
                            stoppingToken);

                        await catalogStore.UpsertAsync(
                            "$idx-et-" + eventType,
                            "Event type",
                            commit is ulong eventTypeCommit ? (long?)eventTypeCommit : null,
                            stoppingToken);
                    }

                    if (commit is ulong commitPosition)
                    {
                        await scanStateStore.SetLastScannedCommitPositionAsync(
                            CheckpointName,
                            unchecked((long)commitPosition),
                            stoppingToken);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Built-in index catalog worker stopped");
        }
    }
}
