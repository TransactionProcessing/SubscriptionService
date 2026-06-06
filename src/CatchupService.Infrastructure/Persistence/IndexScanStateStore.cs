using CatchupService.Domain;
using Microsoft.EntityFrameworkCore;

namespace CatchupService.Infrastructure.Persistence;

public sealed class SqlIndexScanStateStore : IIndexScanStateStore
{
    private readonly IDbContextFactory<CatchupServiceDbContext> _contextFactory;

    public SqlIndexScanStateStore(IDbContextFactory<CatchupServiceDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<long?> GetLastScannedCommitPositionAsync(string secondaryIndexName, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await context.IndexScanStates.FindAsync(new object[] { secondaryIndexName }, cancellationToken);
        return row is null ? null : row.LastScannedCommitPosition;
    }

    public async Task SetLastScannedCommitPositionAsync(string secondaryIndexName, long? commitPosition, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.IndexScanStates.FindAsync(new object[] { secondaryIndexName }, cancellationToken);
        if (existing is null)
        {
            context.IndexScanStates.Add(new IndexScanStateEntity { SecondaryIndexName = secondaryIndexName, LastScannedCommitPosition = commitPosition });
        }
        else
        {
            existing.LastScannedCommitPosition = commitPosition;
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
