using System.Collections.Concurrent;

namespace SubscriptionService.Worker;

public sealed class SubscriptionRestartTracker(TimeSpan restartDelay)
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _restartAfter = new(StringComparer.OrdinalIgnoreCase);

    public void Schedule(string subscriptionId) =>
        this._restartAfter[subscriptionId] = DateTimeOffset.UtcNow + restartDelay;

    public bool IsReady(string subscriptionId)
    {
        if (!this._restartAfter.TryGetValue(subscriptionId, out var restartAfter))
        {
            return true;
        }

        if (restartAfter > DateTimeOffset.UtcNow)
        {
            return false;
        }

        this._restartAfter.TryRemove(subscriptionId, out _);
        return true;
    }

    public void Clear(string subscriptionId) => this._restartAfter.TryRemove(subscriptionId, out _);
}
