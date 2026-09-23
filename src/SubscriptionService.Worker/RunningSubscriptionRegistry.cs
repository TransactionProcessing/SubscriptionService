using System.Collections.Concurrent;
using SubscriptionService.Application;

namespace SubscriptionService.Worker;

public sealed class RunningSubscriptionRegistry : IRunningSubscriptionRegistry
{
    private readonly ConcurrentDictionary<string, byte> _running = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SubscriptionRuntimeFailure> _failures = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SubscriptionRuntimeState> _states = new(StringComparer.OrdinalIgnoreCase);

    public void MarkRunning(string subscriptionId)
    {
        this._failures.TryRemove(subscriptionId, out _);
        this._running[subscriptionId] = 0;
        this._states.AddOrUpdate(
            subscriptionId,
            _ => new SubscriptionRuntimeState(DateTimeOffset.UtcNow, null, null, 0),
            (_, current) => current with { LastStartedAt = DateTimeOffset.UtcNow });
    }

    public void MarkStopped(string subscriptionId, string? reason = null)
    {
        this._running.TryRemove(subscriptionId, out _);
        this._states.AddOrUpdate(
            subscriptionId,
            _ => new SubscriptionRuntimeState(null, DateTimeOffset.UtcNow, reason, 1),
            (_, current) => current with
            {
                LastStoppedAt = DateTimeOffset.UtcNow,
                LastStopReason = reason,
                StopCount = current.StopCount + 1
            });
    }

    public void MarkFailed(string subscriptionId, Exception exception)
    {
        this._failures[subscriptionId] = new SubscriptionRuntimeFailure(exception.Message, DateTimeOffset.UtcNow);
        this.MarkStopped(subscriptionId, exception.Message);
    }

    public bool IsRunning(string subscriptionId) => this._running.ContainsKey(subscriptionId);

    public SubscriptionRuntimeFailure? GetFailure(string subscriptionId) =>
        this._failures.TryGetValue(subscriptionId, out var failure) ? failure : null;

    public SubscriptionRuntimeState? GetState(string subscriptionId) =>
        this._states.TryGetValue(subscriptionId, out var state) ? state : null;
}
