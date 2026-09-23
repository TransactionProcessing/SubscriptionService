using System.Collections.Concurrent;

namespace SubscriptionService.Worker;

public sealed class CountConnectionRegistry
{
    private readonly ConcurrentDictionary<string, CountConnectionSnapshot> _connections = new(StringComparer.OrdinalIgnoreCase);

    public void Upsert(CountConnectionSnapshot snapshot) => this._connections[snapshot.SubscriptionId] = snapshot;

    public CountConnectionSnapshot? Get(string subscriptionId) =>
        this._connections.TryGetValue(subscriptionId, out var snapshot) ? snapshot : null;

    public IReadOnlyCollection<CountConnectionSnapshot> GetAll() =>
        this._connections.Values.OrderBy(x => x.SubscriptionId).ToArray();

    public void Remove(string subscriptionId) => this._connections.TryRemove(subscriptionId, out _);
}
