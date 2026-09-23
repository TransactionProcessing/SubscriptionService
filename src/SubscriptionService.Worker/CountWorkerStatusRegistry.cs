namespace SubscriptionService.Worker;

public sealed class CountWorkerStatusRegistry
{
    private readonly object _gate = new();
    private CountWorkerStatusSnapshot _snapshot = new(0, 0, 0, null, DateTimeOffset.MinValue, null);

    public void SetConfigured(int configuredSubscriptions)
    {
        lock (this._gate)
        {
            this._snapshot = this._snapshot with
            {
                ConfiguredSubscriptions = configuredSubscriptions,
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }
    }

    public void MarkAttempted()
    {
        lock (this._gate)
        {
            this._snapshot = this._snapshot with
            {
                AttemptedSubscriptions = this._snapshot.AttemptedSubscriptions + 1,
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }
    }

    public void MarkStarted()
    {
        lock (this._gate)
        {
            this._snapshot = this._snapshot with
            {
                StartedAt = this._snapshot.StartedAt ?? DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }
    }

    public void MarkFailed(string error)
    {
        lock (this._gate)
        {
            this._snapshot = this._snapshot with
            {
                FailedSubscriptions = this._snapshot.FailedSubscriptions + 1,
                LastError = error,
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }
    }

    public CountWorkerStatusSnapshot GetSnapshot()
    {
        lock (this._gate)
        {
            return this._snapshot;
        }
    }
}
