using CatchupService.Application;
using System.Collections.Concurrent;

namespace CatchupService.Worker;

public sealed class RunningSubscriptionRegistry : IRunningSubscriptionRegistry
{
    private readonly ConcurrentDictionary<string, byte> _running = new(StringComparer.OrdinalIgnoreCase);

    public void MarkRunning(string subscriptionId) => _running[subscriptionId] = 0;

    public void MarkStopped(string subscriptionId) => _running.TryRemove(subscriptionId, out _);

    public bool IsRunning(string subscriptionId) => _running.ContainsKey(subscriptionId);
}