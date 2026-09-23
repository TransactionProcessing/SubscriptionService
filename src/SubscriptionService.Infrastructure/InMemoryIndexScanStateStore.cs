using System.Collections.Concurrent;
using SubscriptionService.Domain;

namespace SubscriptionService.Infrastructure;

public sealed class InMemoryIndexScanStateStore : IIndexScanStateStore
{
    private readonly ConcurrentDictionary<string, long?> _state = new();

    public Task<long?> GetLastScannedCommitPositionAsync(string secondaryIndexName, CancellationToken cancellationToken = default) =>
        Task.FromResult(this._state.TryGetValue(secondaryIndexName, out var v) ? v : null);

    public Task SetLastScannedCommitPositionAsync(string secondaryIndexName, long? commitPosition, CancellationToken cancellationToken = default)
    {
        this._state[secondaryIndexName] = commitPosition;
        return Task.CompletedTask;
    }
}
