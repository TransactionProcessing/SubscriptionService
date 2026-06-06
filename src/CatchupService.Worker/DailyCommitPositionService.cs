using CatchupService.Domain;
using CatchupService.Infrastructure.Persistence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CatchupService.Worker;

public sealed class DailyCommitPositionService : BackgroundService
{
    private readonly IDailyCommitPositionStore _store;
    private readonly ISubscriptionConfigurationStore _configStore;
    private readonly ISubscriptionEventSource _eventSource;
    private readonly IIndexScanStateStore _scanStateStore;
    private readonly TimeSpan _interval = TimeSpan.FromHours(24);

    public DailyCommitPositionService(IDailyCommitPositionStore store, ISubscriptionConfigurationStore configStore, ISubscriptionEventSource eventSource, IIndexScanStateStore scanStateStore)
    {
        _store = store;
        _configStore = configStore;
        _eventSource = eventSource;
        _scanStateStore = scanStateStore;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var subs = await _configStore.GetSubscriptionsAsync(stoppingToken);
                // Use UTC date for daily buckets; could be adjusted to a timezone if configured.
                var today = DateTime.UtcNow.Date;

                // Build distinct list of secondary indexes to scan
                var indexToSubs = subs
                    .Where(s => !string.IsNullOrWhiteSpace(s.SecondaryIndexName))
                    .GroupBy(s => s.SecondaryIndexName)
                    .ToDictionary(g => g.Key!, g => g.ToArray(), StringComparer.OrdinalIgnoreCase);

                // Process each index concurrently with a bounded degree of parallelism.
                var maxConcurrency = Math.Max(1, Environment.ProcessorCount / 2);
                using var semaphore = new SemaphoreSlim(maxConcurrency);
                var tasks = new List<Task>(indexToSubs.Count);

                foreach (var kvp in indexToSubs)
                {
                    var index = kvp.Key;
                    var subscribers = kvp.Value;

                    tasks.Add(Task.Run(async () =>
                    {
                        await semaphore.WaitAsync(stoppingToken);
                        try
                        {
                            // Skip if already stored for today for any subscription (we write per-subscription rows but capture once per index)
                            var existing = await _store.GetCommitPositionForDateAsync(subscribers[0].SubscriptionId, index, today, stoppingToken);
                            if (existing is not null) return;

                            // Resume scan from last scanned commit position to avoid starting from beginning on restart
                            var lastScanned = await _scanStateStore.GetLastScannedCommitPositionAsync(index, stoppingToken);

                            // Scan events for this index, starting from lastScanned. We'll resubscribe in short windows until we see no events
                            // for a few consecutive windows which indicates we've caught up. This allows first-run scans to start at the beginning
                            // of the index and progress forward while persisting progress so restarts resume from the last processed position.
                            var consecutiveEmpty = 0;
                            const int maxEmptyWindows = 3;
                            var scanLastScanned = lastScanned;

                            while (!stoppingToken.IsCancellationRequested && consecutiveEmpty < maxEmptyWindows)
                            {
                                var foundAny = false;
                                try
                                {
                                    var checkpointState = new CheckpointState(scanLastScanned, 0, null);
                                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                                    cts.CancelAfter(TimeSpan.FromSeconds(10));

                                    await _eventSource.SubscribeAsync(subscribers[0], checkpointState, async (@event, ct) =>
                                    {
                                        foundAny = true;
                                        var commit = @event.CommitPosition;
                                        var eventDate = @event.OccurredAt.UtcDateTime.Date;

                                        // For each subscription that targets this index, write a daily row if missing
                                        foreach (var s in subscribers)
                                        {
                                            var existingRow = await _store.GetCommitPositionForDateAsync(s.SubscriptionId, index, eventDate, ct);
                                            if (existingRow is null)
                                            {
                                                await _store.UpsertAsync(s.SubscriptionId, index, eventDate, commit, ct);
                                            }
                                        }

                                        // advance scan pointer and persist it so restarts resume after this event
                                        if (commit is not null)
                                        {
                                            scanLastScanned = commit;
                                            await _scanStateStore.SetLastScannedCommitPositionAsync(index, scanLastScanned, ct);
                                        }

                                        return true; // continue subscription
                                    }, cts.Token);
                                }
                                catch
                                {
                                    // ignore transient subscribe errors
                                }

                                if (foundAny)
                                {
                                    consecutiveEmpty = 0;
                                }
                                else
                                {
                                    consecutiveEmpty++;
                                }
                            }
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }, stoppingToken));
                }

                try
                {
                    await Task.WhenAll(tasks);
                }
                catch (OperationCanceledException)
                {
                    // cancellation requested - continue to shutdown
                }
            }
            catch
            {
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }
}
