using CatchupService.Infrastructure.Persistence;
using CatchupService.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using KurrentDB.Client;

namespace CatchupService.Worker;

public sealed class StreamEventCountService : BackgroundService
{
    private readonly KurrentDBClient _client;
    private readonly IDbContextFactory<CatchupServiceDbContext> _dbFactory;
    private readonly ILogger<StreamEventCountService> _logger;
    private readonly TimeSpan _interval;

    public StreamEventCountService(KurrentDBClient client, IDbContextFactory<CatchupServiceDbContext> dbFactory, ILogger<StreamEventCountService> logger)
    {
        _client = client;
        _dbFactory = dbFactory;
        _logger = logger;
        _interval = TimeSpan.FromSeconds(60);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await UpdateCounts(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update stream event counts");
            }

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (OperationCanceledException) { }
        }
    }

    private async Task UpdateCounts(CancellationToken cancellationToken)
    {
        // Read distinct secondary index names from subscription configurations
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var indexes = await db.SubscriptionConfigurations
            .AsNoTracking()
            .Where(x => x.Enabled)
            .Select(x => x.SecondaryIndexName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToArrayAsync(cancellationToken);

        foreach (var idx in indexes)
        {
            // Read to the current end of the index; do not limit maxCount so the enumeration runs until caught-up
            var read = _client.ReadAllAsync(Direction.Forwards, Position.Start, StreamFilter.Prefix(idx));
            long total = 0;

            await foreach (var message in read.WithCancellation(cancellationToken))
            {
                if (message is ResolvedEvent)
                {
                    total++;
                }
            }

            await upsertCount(idx, total, cancellationToken);
            _logger.LogInformation("Updated count for {Idx} = {Count}", idx, total);
        }
    }

    private async Task upsertCount(string idx, long total, CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.StreamEventCounts.FindAsync(new object[] { idx }, cancellationToken);
        if (existing is null)
        {
            db.StreamEventCounts.Add(new StreamEventCountEntity { SecondaryIndexName = idx, TotalCount = total, UpdatedAt = DateTimeOffset.UtcNow });
        }
        else
        {
            existing.TotalCount = total;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
