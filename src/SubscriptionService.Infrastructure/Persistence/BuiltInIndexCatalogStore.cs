using Microsoft.EntityFrameworkCore;
using SubscriptionService.Domain;

namespace SubscriptionService.Infrastructure.Persistence;

public sealed class SqlBuiltInIndexCatalogStore(IDbContextFactory<CatchupServiceDbContext> contextFactory) : IBuiltInIndexCatalogStore
{
    public async Task<IReadOnlyList<BuiltInIndexCatalogEntry>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.BuiltInIndexCatalog
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new BuiltInIndexCatalogEntry(x.Name, x.Type, x.LastSeenCommitPosition, x.UpdatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task UpsertAsync(string name, string type, long? lastSeenCommitPosition, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.BuiltInIndexCatalog.FindAsync([name], cancellationToken);
        if (existing is null)
        {
            context.BuiltInIndexCatalog.Add(new BuiltInIndexCatalogEntity
            {
                Name = name,
                Type = type,
                LastSeenCommitPosition = lastSeenCommitPosition,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            existing.Type = type;
            existing.LastSeenCommitPosition = lastSeenCommitPosition;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
