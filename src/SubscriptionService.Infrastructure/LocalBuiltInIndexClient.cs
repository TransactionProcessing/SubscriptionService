using SubscriptionService.Application;
using SubscriptionService.Domain;

namespace SubscriptionService.Infrastructure;

public sealed class LocalBuiltInIndexClient(IBuiltInIndexCatalogStore store) : IEventStoreBuiltInIndexClient
{
    public async Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
        (await store.GetAllAsync(cancellationToken))
        .Where(x => string.Equals(x.Type, "Category", StringComparison.OrdinalIgnoreCase))
        .Select(x => x.Name["$idx-ce-".Length..])
        .ToArray();

    public async Task<IReadOnlyList<string>> GetEventTypesAsync(CancellationToken cancellationToken = default) =>
        (await store.GetAllAsync(cancellationToken))
        .Where(x => string.Equals(x.Type, "Event type", StringComparison.OrdinalIgnoreCase))
        .Select(x => x.Name["$idx-et-".Length..])
        .ToArray();
}
