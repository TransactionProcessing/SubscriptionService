using System.Collections.Concurrent;
using CatchupService.Application;

namespace CatchupService.Worker;

public sealed class WorkerRuntimeRegistry
{
    private readonly ConcurrentDictionary<string, SubscriptionRuntime> _runtimes = new(StringComparer.OrdinalIgnoreCase);

    public void Register(string subscriptionId, SubscriptionRuntime runtime) => _runtimes[subscriptionId] = runtime;

    public void Unregister(string subscriptionId) => _runtimes.TryRemove(subscriptionId, out _);

    public SubscriptionRuntime? Get(string subscriptionId) => _runtimes.TryGetValue(subscriptionId, out var runtime) ? runtime : null;
}