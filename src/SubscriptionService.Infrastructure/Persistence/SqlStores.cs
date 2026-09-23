using Microsoft.EntityFrameworkCore;
using SubscriptionService.Domain;

namespace SubscriptionService.Infrastructure.Persistence;

public sealed class SqlSubscriptionConfigurationStore : ISubscriptionConfigurationStore
{
    private readonly IDbContextFactory<CatchupServiceDbContext> _contextFactory;

    public SqlSubscriptionConfigurationStore(IDbContextFactory<CatchupServiceDbContext> contextFactory)
    {
        this._contextFactory = contextFactory;
    }


    public async Task<IReadOnlyCollection<SubscriptionDefinition>> GetSubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await this._contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await context.SubscriptionConfigurations
            .AsNoTracking()
            .Include(x => x.Endpoint)
            .OrderBy(x => x.SubscriptionId)
            .ToArrayAsync(cancellationToken);
        return entities.Select(x => x.ToDomain()).ToArray();
    }

    public async Task UpsertAsync(SubscriptionDefinition subscription, CancellationToken cancellationToken = default)
    {
        await using var context = await this._contextFactory.CreateDbContextAsync(cancellationToken);
        var endpointId = await ResolveEndpointIdAsync(context, subscription, cancellationToken);
        subscription = subscription with { EndpointId = endpointId };
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

    private static async Task<int> ResolveEndpointIdAsync(CatchupServiceDbContext context, SubscriptionDefinition subscription, CancellationToken cancellationToken)
    {
        if (subscription.EndpointId is > 0)
        {
            if (!await context.Endpoints.AnyAsync(x => x.EndpointId == subscription.EndpointId.Value, cancellationToken))
                throw new InvalidOperationException($"Endpoint '{subscription.EndpointId}' was not found.");
            return subscription.EndpointId.Value;
        }

        var authenticationJson = subscription.Authentication is null ? null : JsonPersistence.Serialize(subscription.Authentication.Parameters);
        var authenticationScheme = subscription.Authentication?.Scheme;
        var endpoint = await context.Endpoints.FirstOrDefaultAsync(x => x.Url == subscription.EndpointUrl && x.AuthenticationScheme == authenticationScheme && x.AuthenticationParametersJson == authenticationJson, cancellationToken);
        if (endpoint is not null)
            return endpoint.EndpointId;

        context.Endpoints.Add(new EndpointEntity
        {
            Name = subscription.SubscriptionId,
            Url = subscription.EndpointUrl,
            AuthenticationScheme = subscription.Authentication?.Scheme,
            AuthenticationParametersJson = authenticationJson
        });
        await context.SaveChangesAsync(cancellationToken);
        return context.Endpoints.Local.Last().EndpointId;
    }

    public async Task SetOperationalStateAsync(
        string subscriptionId,
        SubscriptionOperationalState operationalState,
        string? operationalReason = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await this._contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.SubscriptionConfigurations.FindAsync(new object[] { subscriptionId }, cancellationToken);

        if (existing is null)
        {
            return;
        }

        existing.Enabled = operationalState == SubscriptionOperationalState.Healthy;
        existing.OperationalState = operationalState.ToString();
        existing.OperationalReason = operationalReason;

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        await using var context = await this._contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.SubscriptionConfigurations.FindAsync(new object[] { subscriptionId }, cancellationToken);

        if (existing is null)
        {
            return;
        }

        context.SubscriptionConfigurations.Remove(existing);
        await context.SaveChangesAsync(cancellationToken);
    }
}

public sealed class SqlEndpointStore(IDbContextFactory<CatchupServiceDbContext> contextFactory) : IEndpointStore
{
    public async Task<IReadOnlyCollection<EndpointDefinition>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await context.Endpoints.AsNoTracking().OrderBy(x => x.Name).ToArrayAsync(cancellationToken);
        return entities.Select(x => x.ToDomain()).ToArray();
    }

    public async Task<EndpointDefinition?> GetAsync(int endpointId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var endpoint = await context.Endpoints.AsNoTracking().SingleOrDefaultAsync(x => x.EndpointId == endpointId, cancellationToken);
        return endpoint?.ToDomain();
    }

    public async Task UpsertAsync(EndpointDefinition endpoint, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.Endpoints.FindAsync([endpoint.EndpointId], cancellationToken);
        if (existing is null) context.Endpoints.Add(endpoint.ToEntity());
        else context.Entry(existing).CurrentValues.SetValues(endpoint.ToEntity());
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> RemoveAsync(int endpointId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var endpoint = await context.Endpoints.Include(x => x.Subscriptions).SingleOrDefaultAsync(x => x.EndpointId == endpointId, cancellationToken);
        if (endpoint is null) return false;
        if (endpoint.Subscriptions.Count != 0) throw new InvalidOperationException("The endpoint is still used by one or more subscriptions.");
        context.Endpoints.Remove(endpoint);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public sealed class SqlSubscriptionEventLogStore(IDbContextFactory<CatchupServiceDbContext> contextFactory) : ISubscriptionEventLogStore
{
    public async Task AddAsync(SubscriptionEventLog eventLog, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.SubscriptionEventLogs.Add(new SubscriptionEventLogEntity
        {
            EventId = eventLog.EventId,
            SubscriptionId = eventLog.SubscriptionId,
            Payload = eventLog.Payload,
            AddressSentTo = eventLog.AddressSentTo,
            ResponseStatusCode = eventLog.ResponseStatusCode,
            EventType = eventLog.EventType,
            ResponseBody = eventLog.ResponseBody,
            ProcessedAt = eventLog.ProcessedAt,
            DeliveryAttempt = eventLog.DeliveryAttempt,
            IsReplay = eventLog.IsReplay
        });
        await context.SaveChangesAsync(cancellationToken);
    }
}

public sealed class SqlCheckpointStore : ICheckpointStore
{
    private readonly IDbContextFactory<CatchupServiceDbContext> _contextFactory;

    public SqlCheckpointStore(IDbContextFactory<CatchupServiceDbContext> contextFactory)
    {
        this._contextFactory = contextFactory;
    }

    public async Task<CheckpointState> GetCheckpointAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        await using var context = await this._contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await context.SubscriptionCheckpoints
            .AsNoTracking()
            .Where(x => x.SubscriptionId == subscriptionId)
            .Select(x => new { x.CommitPosition, x.PreparePosition, x.ProcessedCount, x.CheckpointReason })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null
            ? new CheckpointState(null, 0L, null)
            : new CheckpointState(row.CommitPosition, row.ProcessedCount, row.CheckpointReason, row.PreparePosition);
    }

    public async Task SaveCheckpointAsync(
        string subscriptionId,
        long? commitPosition,
        long processedCount,
        string? checkpointReason = null,
        long? preparePosition = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await this._contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.SubscriptionCheckpoints.FindAsync(new object[] { subscriptionId }, cancellationToken);

        if (existing is null)
        {
            context.SubscriptionCheckpoints.Add(new SubscriptionCheckpointEntity
            {
                SubscriptionId = subscriptionId,
                CommitPosition = commitPosition,
                PreparePosition = preparePosition,
                ProcessedCount = processedCount,
                CheckpointReason = checkpointReason
            });
        }
        else
        {
            existing.CommitPosition = commitPosition;
            existing.PreparePosition = preparePosition;
            existing.ProcessedCount = processedCount; // persist the latest total, don't accumulate across saves
            existing.CheckpointReason = checkpointReason;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        await using var context = await this._contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.SubscriptionCheckpoints.FindAsync(new object[] { subscriptionId }, cancellationToken);

        if (existing is null)
        {
            return;
        }

        context.SubscriptionCheckpoints.Remove(existing);
        await context.SaveChangesAsync(cancellationToken);
    }
}

public sealed class SqlParkedEventStore : IParkedEventStore
{
    private readonly IDbContextFactory<CatchupServiceDbContext> _contextFactory;

    public SqlParkedEventStore(IDbContextFactory<CatchupServiceDbContext> contextFactory)
    {
        this._contextFactory = contextFactory;
    }

    public async Task ParkAsync(ParkedEvent parkedEvent, CancellationToken cancellationToken = default)
    {
        await using var context = await this._contextFactory.CreateDbContextAsync(cancellationToken);
        context.ParkedEvents.Add(parkedEvent.ToEntity());
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<ParkedEvent>> GetParkedEventsAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        await using var context = await this._contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.ParkedEvents
            .AsNoTracking()
            .Where(x => x.SubscriptionId == subscriptionId && x.IsDeleted == false)
            .OrderBy(x => x.SequenceNumber)
            .Select(x => x.ToDomain())
            .ToArrayAsync(cancellationToken);
    }

    public async Task RemoveParkedEventAsync(string subscriptionId, Guid parkedEventId, ISubscriptionConfigurationStore configurationStore, CancellationToken cancellationToken = default)
    {
        await using var context = await this._contextFactory.CreateDbContextAsync(cancellationToken);
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
        this._contextFactory = contextFactory;
    }

    public async Task<ReplaySession> StartAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        await using var context = await this._contextFactory.CreateDbContextAsync(cancellationToken);
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
        await using var context = await this._contextFactory.CreateDbContextAsync(cancellationToken);
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
        await using var context = await this._contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.ReplaySessions
            .AsNoTracking()
            .Where(x => x.SubscriptionId == subscriptionId && x.CompletedAt == null)
            .Select(x => x.ToDomain())
            .ToArrayAsync(cancellationToken);
    }
}
