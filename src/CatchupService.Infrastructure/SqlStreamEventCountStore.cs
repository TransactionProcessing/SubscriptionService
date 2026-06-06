using CatchupService.Application;
using Microsoft.EntityFrameworkCore;

namespace CatchupService.Infrastructure.Persistence;

public sealed class SqlStreamEventCountStore : IStreamEventCountStore
{
    private readonly IDbContextFactory<CatchupServiceDbContext> _contextFactory;

    public SqlStreamEventCountStore(IDbContextFactory<CatchupServiceDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<long?> GetTotalForIndexAsync(string secondaryIndexName, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await context.StreamEventCounts
            .AsNoTracking()
            .Where(x => x.SecondaryIndexName == secondaryIndexName)
            .Select(x => (long?)x.TotalCount)
            .SingleOrDefaultAsync(cancellationToken);

        return row;
    }
}
