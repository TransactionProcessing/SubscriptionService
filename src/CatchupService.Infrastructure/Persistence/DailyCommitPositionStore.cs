using CatchupService.Domain;
using Microsoft.EntityFrameworkCore;

namespace CatchupService.Infrastructure.Persistence;

public sealed class SqlDailyCommitPositionStore : IDailyCommitPositionStore
{
    private readonly IDbContextFactory<CatchupServiceDbContext> _contextFactory;

    public SqlDailyCommitPositionStore(IDbContextFactory<CatchupServiceDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task UpsertAsync(string subscriptionId, string secondaryIndexName, DateTime date, long? commitPosition, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.DailyCommitPositions.FindAsync(new object[] { subscriptionId, secondaryIndexName, date }, cancellationToken);
        if (existing is null)
        {
            context.DailyCommitPositions.Add(new DailyCommitPositionEntity { SubscriptionId = subscriptionId, SecondaryIndexName = secondaryIndexName, Date = date, CommitPosition = commitPosition });
        }
        else
        {
            existing.CommitPosition = commitPosition;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<long?> GetCommitPositionForDateAsync(string subscriptionId, string secondaryIndexName, DateTime date, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await context.DailyCommitPositions.FindAsync(new object[] { subscriptionId, secondaryIndexName, date }, cancellationToken);
        return row is null ? null : row.CommitPosition;
    }

    public async Task<IReadOnlyCollection<DailyCommitPositionRecord>> GetPositionsAsync(string? subscriptionId, string? secondaryIndexName, DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var q = context.DailyCommitPositions.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(subscriptionId)) q = q.Where(x => x.SubscriptionId == subscriptionId);
        if (!string.IsNullOrWhiteSpace(secondaryIndexName)) q = q.Where(x => x.SecondaryIndexName == secondaryIndexName);
        q = q.Where(x => x.Date >= fromDate && x.Date <= toDate);
        var rows = await q.OrderBy(x => x.Date).ToArrayAsync(cancellationToken);
        return rows.Select(x => new DailyCommitPositionRecord(x.SubscriptionId, x.SecondaryIndexName, x.Date, x.CommitPosition)).ToArray();
    }

    public async Task DeleteOlderThanAsync(DateTime threshold, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var old = await context.DailyCommitPositions.Where(x => x.Date < threshold).ToArrayAsync(cancellationToken);
        if (old.Length == 0) return;
        context.DailyCommitPositions.RemoveRange(old);
        await context.SaveChangesAsync(cancellationToken);
    }
}
