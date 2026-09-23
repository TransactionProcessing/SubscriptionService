namespace SubscriptionService.Application;

public interface IEventStoreBuiltInIndexClient
{
    Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetEventTypesAsync(CancellationToken cancellationToken = default);
}
