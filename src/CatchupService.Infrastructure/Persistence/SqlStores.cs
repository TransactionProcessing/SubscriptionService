using CatchupService.Domain;
using Microsoft.EntityFrameworkCore;

namespace CatchupService.Infrastructure.Persistence;

public sealed class SqlSubscriptionConfigurationStore(IDbContextFactory<CatchupServiceDbContext> contextFactory) : ISubscriptionConfigurationStore
{
    public async Task<IReadOnlyCollection<SubscriptionDefinition>> GetSubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.SubscriptionConfigurations
            .AsNoTracking()
            .OrderBy(x => x.SubscriptionId)
            .Select(x => x.ToDomain())
            .ToArrayAsync(cancellationToken);
    }

    public async Task UpsertAsync(SubscriptionDefinition subscription, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
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
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.SubscriptionConfigurations.FindAsync(new object[] { subscriptionId }, cancellationToken);

        if (existing is null)
        {
            return;
        }

        context.SubscriptionConfigurations.Remove(existing);
        await context.SaveChangesAsync(cancellationToken);
    }
}

public sealed class SqlCheckpointStore(IDbContextFactory<CatchupServiceDbContext> contextFactory) : ICheckpointStore
{
    public async Task<long> GetCheckpointAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var checkpoint = await context.SubscriptionCheckpoints
            .AsNoTracking()
            .Where(x => x.SubscriptionId == subscriptionId)
            .Select(x => (long?)x.Checkpoint)
            .SingleOrDefaultAsync(cancellationToken);

        return checkpoint ?? 0L;
    }

    public async Task SaveCheckpointAsync(string subscriptionId, long checkpoint, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.SubscriptionCheckpoints.FindAsync(new object[] { subscriptionId }, cancellationToken);

        if (existing is null)
        {
            context.SubscriptionCheckpoints.Add(new SubscriptionCheckpointEntity
            {
                SubscriptionId = subscriptionId,
                Checkpoint = checkpoint
            });
        }
        else
        {
            existing.Checkpoint = checkpoint;
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}

public sealed class SqlParkedEventStore(IDbContextFactory<CatchupServiceDbContext> contextFactory) : IParkedEventStore
{
    public async Task ParkAsync(ParkedEvent parkedEvent, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.ParkedEvents.Add(parkedEvent.ToEntity());
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<ParkedEvent>> GetParkedEventsAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.ParkedEvents
            .AsNoTracking()
            .Where(x => x.SubscriptionId == subscriptionId)
            .OrderBy(x => x.SequenceNumber)
            .Select(x => x.ToDomain())
            .ToArrayAsync(cancellationToken);
    }
}

public sealed class SqlReplaySessionStore(IDbContextFactory<CatchupServiceDbContext> contextFactory) : IReplaySessionStore
{
    public async Task<ReplaySession> StartAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
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
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.ReplaySessions.FindAsync(new object[] { replaySessionId }, cancellationToken);

        if (existing is null)
        {
            return;
        }

        existing.CompletedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }
}
