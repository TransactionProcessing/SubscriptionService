using CatchupService.Domain;
using CatchupService.Application;
using Microsoft.EntityFrameworkCore;

namespace CatchupService.Infrastructure.Persistence;

public sealed class SqlSubscriptionConfigurationStore : ISubscriptionConfigurationStore
{
    private readonly IDbContextFactory<CatchupServiceDbContext> _contextFactory;

    public SqlSubscriptionConfigurationStore(IDbContextFactory<CatchupServiceDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }


    public async Task<IReadOnlyCollection<SubscriptionDefinition>> GetSubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.SubscriptionConfigurations
            .AsNoTracking()
            .OrderBy(x => x.SubscriptionId)
            .Select(x => x.ToDomain())
            .ToArrayAsync(cancellationToken);
    }

    public async Task UpsertAsync(SubscriptionDefinition subscription, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.SubscriptionConfigurations.FindAsync(new object[] { subscription.SubscriptionId }, cancellationToken);

        if (existing is null)
        {
            context.SubscriptionConfigurations.Add(subscription.ToEntity());
        }
        else
        {
            var updated = subscription.ToEntity();
            context.Entry(existing).CurrentValues.SetValues(updated);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.SubscriptionConfigurations.FindAsync(new object[] { subscriptionId }, cancellationToken);

        if (existing is null)
        {
            return;
        }

        context.SubscriptionConfigurations.Remove(existing);
        await context.SaveChangesAsync(cancellationToken);
    }
}

public sealed class SqlCheckpointStore : ICheckpointStore
{
    private readonly IDbContextFactory<CatchupServiceDbContext> _contextFactory;

    public SqlCheckpointStore(IDbContextFactory<CatchupServiceDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<CheckpointState> GetCheckpointAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await context.SubscriptionCheckpoints
            .AsNoTracking()
            .Where(x => x.SubscriptionId == subscriptionId)
            .Select(x => new { x.CommitPosition, x.ProcessedCount })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null
            ? new CheckpointState(null, 0L, null)
            : new CheckpointState(row.CommitPosition, row.ProcessedCount, null);
    }

    public async Task SaveCheckpointAsync(string subscriptionId, long? commitPosition, long processedCount, string? checkpointReason = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.SubscriptionCheckpoints.FindAsync(new object[] { subscriptionId }, cancellationToken);

        if (existing is null)
        {
            context.SubscriptionCheckpoints.Add(new SubscriptionCheckpointEntity
            {
                SubscriptionId = subscriptionId,
                CommitPosition = commitPosition,
                ProcessedCount = processedCount,
                CheckpointReason = checkpointReason
            });
        }
        else
        {
            existing.CommitPosition = commitPosition;
            existing.ProcessedCount +=processedCount; // set to the persisted total
            existing.CheckpointReason = checkpointReason;
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}

public sealed class SqlParkedEventStore : IParkedEventStore
{
    private readonly IDbContextFactory<CatchupServiceDbContext> _contextFactory;

    public SqlParkedEventStore(IDbContextFactory<CatchupServiceDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task ParkAsync(ParkedEvent parkedEvent, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        context.ParkedEvents.Add(parkedEvent.ToEntity());
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<ParkedEvent>> GetParkedEventsAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.ParkedEvents
            .AsNoTracking()
            .Where(x => x.SubscriptionId == subscriptionId && x.IsDeleted == false)
            .OrderBy(x => x.SequenceNumber)
            .Select(x => x.ToDomain())
            .ToArrayAsync(cancellationToken);
    }

    public async Task RemoveParkedEventAsync(string subscriptionId, Guid parkedEventId, ISubscriptionConfigurationStore configurationStore, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.ParkedEvents.FindAsync(new object[] { parkedEventId }, cancellationToken);
        if (existing is null)
        {
            return;
        }

        var subscriptions = await configurationStore.GetSubscriptionsAsync(cancellationToken);
        var subscription = subscriptions.FirstOrDefault(x => string.Equals(x.SubscriptionId, subscriptionId, StringComparison.OrdinalIgnoreCase));
        var softDelete = subscription?.SoftDeleteParked ?? true;

        if (softDelete)
        {
            existing.IsDeleted = true;
        }
        else
        {
            context.ParkedEvents.Remove(existing);
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}

public sealed class SqlReplaySessionStore : IReplaySessionStore
{
    private readonly IDbContextFactory<CatchupServiceDbContext> _contextFactory;

    public SqlReplaySessionStore(IDbContextFactory<CatchupServiceDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<ReplaySession> StartAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = new ReplaySessionEntity
        {
            ReplaySessionId = Guid.NewGuid(),
            SubscriptionId = subscriptionId,
            StartedAt = DateTimeOffset.UtcNow
        };

        context.ReplaySessions.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
        return entity.ToDomain();
    }

    public async Task CompleteAsync(Guid replaySessionId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.ReplaySessions.FindAsync(new object[] { replaySessionId }, cancellationToken);

        if (existing is null)
        {
            return;
        }

        existing.CompletedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<ReplaySession>> GetActiveSessionsAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.ReplaySessions
            .AsNoTracking()
            .Where(x => x.SubscriptionId == subscriptionId && x.CompletedAt == null)
            .Select(x => x.ToDomain())
            .ToArrayAsync(cancellationToken);
    }
}
