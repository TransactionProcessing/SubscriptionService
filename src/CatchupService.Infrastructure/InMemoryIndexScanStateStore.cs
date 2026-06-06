using CatchupService.Domain;
using System.Collections.Concurrent;

namespace CatchupService.Infrastructure;

public sealed class InMemoryIndexScanStateStore : IIndexScanStateStore
{
    private readonly ConcurrentDictionary<string, long?> _state = new();

    public Task<long?> GetLastScannedCommitPositionAsync(string secondaryIndexName, CancellationToken cancellationToken = default) =>
        Task.FromResult(_state.TryGetValue(secondaryIndexName, out var v) ? v : null);

    public Task SetLastScannedCommitPositionAsync(string secondaryIndexName, long? commitPosition, CancellationToken cancellationToken = default)
    {
        _state[secondaryIndexName] = commitPosition;
        return Task.CompletedTask;
    }
}
