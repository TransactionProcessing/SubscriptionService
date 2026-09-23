using System.Collections.Concurrent;
using SubscriptionService.Application;

namespace SubscriptionService.Worker;

public sealed class WorkerRuntimeRegistry
{
    private readonly ConcurrentDictionary<string, SubscriptionRuntime> _runtimes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellations = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _stopped = new(StringComparer.OrdinalIgnoreCase);

    public void Register(string subscriptionId, SubscriptionRuntime runtime)
    {
        this._runtimes[subscriptionId] = runtime;
    }

    public void Register(string subscriptionId, SubscriptionRuntime runtime, CancellationTokenSource cancellationTokenSource)
    {
        this._runtimes[subscriptionId] = runtime;
        this._cancellations[subscriptionId] = cancellationTokenSource;
        this._stopped[subscriptionId] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public void Unregister(string subscriptionId)
    {
        this._runtimes.TryRemove(subscriptionId, out _);
        this._cancellations.TryRemove(subscriptionId, out _);
        if (this._stopped.TryRemove(subscriptionId, out var stopped))
        {
            stopped.TrySetResult();
        }
    }

    public bool RequestStop(string subscriptionId)
    {
        if (!this._cancellations.TryGetValue(subscriptionId, out var cancellationTokenSource))
        {
            return false;
        }

        _ = cancellationTokenSource.CancelAsync();
        return true;
    }

    public async Task<bool> RequestStopAsync(
        string subscriptionId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (!this._cancellations.TryGetValue(subscriptionId, out var cancellationTokenSource)
            || !this._stopped.TryGetValue(subscriptionId, out var stopped))
        {
            return false;
        }

        await cancellationTokenSource.CancelAsync();

        try
        {
            await stopped.Task.WaitAsync(timeout, cancellationToken);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public bool TryGetLive(string subscriptionId, out SubscriptionRuntime? runtime)
    {
        if (this._cancellations.ContainsKey(subscriptionId)
            && this._runtimes.TryGetValue(subscriptionId, out runtime))
        {
            return true;
        }

        runtime = null;
        return false;
    }

    public SubscriptionRuntime? Get(string subscriptionId) => this._runtimes.TryGetValue(subscriptionId, out var runtime) ? runtime : null;

    public IReadOnlyCollection<KeyValuePair<string, SubscriptionRuntime>> GetAll() => this._runtimes.ToArray();
}
