using Microsoft.EntityFrameworkCore;
using SubscriptionService.Application;
using SubscriptionService.Infrastructure.Persistence;

namespace SubscriptionService.Infrastructure;

public sealed class SqlStreamEventCountStore : IStreamEventCountStore
{
    private readonly IDbContextFactory<CatchupServiceDbContext> _contextFactory;

    public SqlStreamEventCountStore(IDbContextFactory<CatchupServiceDbContext> contextFactory)
    {
        this._contextFactory = contextFactory;
    }

    public async Task<long?> GetTotalForSubscriptionAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        var state = await this.GetStateAsync(subscriptionId, cancellationToken);
        return state?.TotalCount;
    }

    public async Task<StreamEventCountState?> GetStateAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        await using var context = await this._contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.StreamEventCounts
            .AsNoTracking()
            .Where(x => x.SubscriptionId == subscriptionId)
            .Select(x => new StreamEventCountState(x.TotalCount, x.LastScannedCommitPosition, x.UpdatedAt))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task UpsertAsync(
        string subscriptionId,
        string secondaryIndexName,
        long totalCount,
        long? lastScannedCommitPosition,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        await using var context = await this._contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.StreamEventCounts.FindAsync(new object[] { subscriptionId }, cancellationToken);

        if (existing is null)
        {
            context.StreamEventCounts.Add(new StreamEventCountEntity
            {
                SubscriptionId = subscriptionId,
                SecondaryIndexName = secondaryIndexName,
                TotalCount = totalCount,
                LastScannedCommitPosition = lastScannedCommitPosition,
                UpdatedAt = updatedAt
            });
        }
        else
        {
            existing.SubscriptionId = subscriptionId;
            existing.SecondaryIndexName = secondaryIndexName;
            existing.TotalCount = totalCount;
            existing.LastScannedCommitPosition = lastScannedCommitPosition;
            existing.UpdatedAt = updatedAt;
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
