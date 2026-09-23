using System.Collections.Concurrent;
using SubscriptionService.Domain;

namespace SubscriptionService.Infrastructure;

public sealed class InMemorySubscriptionConfigurationStore : ISubscriptionConfigurationStore
{
    private readonly ConcurrentDictionary<string, SubscriptionDefinition> _subscriptions = new();

    public InMemorySubscriptionConfigurationStore(IEnumerable<SubscriptionDefinition>? subscriptions = null)
    {
        foreach (var subscription in subscriptions ?? Enumerable.Empty<SubscriptionDefinition>())
        {
            this._subscriptions[subscription.SubscriptionId] = subscription;
        }
    }

    public Task<IReadOnlyCollection<SubscriptionDefinition>> GetSubscriptionsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyCollection<SubscriptionDefinition>>(this._subscriptions.Values.OrderBy(x => x.SubscriptionId).ToArray());

    public Task UpsertAsync(SubscriptionDefinition subscription, CancellationToken cancellationToken = default)
    {
        this._subscriptions[subscription.SubscriptionId] = subscription;
        return Task.CompletedTask;
    }

    public Task SetOperationalStateAsync(
        string subscriptionId,
        SubscriptionOperationalState operationalState,
        string? operationalReason = null,
        CancellationToken cancellationToken = default)
    {
        if (this._subscriptions.TryGetValue(subscriptionId, out var existing))
        {
            this._subscriptions[subscriptionId] = existing with
            {
                Enabled = operationalState == SubscriptionOperationalState.Healthy,
                OperationalState = operationalState,
                OperationalReason = operationalReason
            };
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        this._subscriptions.TryRemove(subscriptionId, out _);
        return Task.CompletedTask;
    }

    public void Upsert(SubscriptionDefinition subscription) => this._subscriptions[subscription.SubscriptionId] = subscription;

    public void Remove(string subscriptionId) => this._subscriptions.TryRemove(subscriptionId, out _);
}

public sealed class InMemorySubscriptionEventLogStore : ISubscriptionEventLogStore
{
    private readonly ConcurrentQueue<SubscriptionEventLog> _entries = new();

    public IReadOnlyList<SubscriptionEventLog> Entries => this._entries.ToArray();

    public Task AddAsync(SubscriptionEventLog eventLog, CancellationToken cancellationToken = default)
    {
        this._entries.Enqueue(eventLog);
        return Task.CompletedTask;
    }
}

public sealed class InMemoryCheckpointStore : ICheckpointStore
{
    private readonly ConcurrentDictionary<string, long?> _commitPositions = new();
    private readonly ConcurrentDictionary<string, long?> _preparePositions = new();
    private readonly ConcurrentDictionary<string, long> _processedCounts = new();

    public Task<CheckpointState> GetCheckpointAsync(string subscriptionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new CheckpointState(
            this._commitPositions.TryGetValue(subscriptionId, out var commitPos) && commitPos.HasValue ? commitPos : 0L,
            this._processedCounts.TryGetValue(subscriptionId, out var pc) ? pc : 0L,
            null,
            this._preparePositions.TryGetValue(subscriptionId, out var preparePos) && preparePos.HasValue ? preparePos : null));

    public Task SaveCheckpointAsync(
        string subscriptionId,
        long? commitPosition,
        long processedCount,
        string? checkpointReason = null,
        long? preparePosition = null,
        CancellationToken cancellationToken = default)
    {
        this._commitPositions[subscriptionId] = commitPosition;
        this._preparePositions[subscriptionId] = preparePosition;
        this._processedCounts[subscriptionId] = processedCount;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        this._commitPositions.TryRemove(subscriptionId, out _);
        this._preparePositions.TryRemove(subscriptionId, out _);
        this._processedCounts.TryRemove(subscriptionId, out _);
        return Task.CompletedTask;
    }
}

public sealed class InMemoryParkedEventStore : IParkedEventStore
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<ParkedEvent>> _parkedEvents = new();
    private readonly ConcurrentDictionary<Guid, bool> _deleted = new();

    public Task ParkAsync(ParkedEvent parkedEvent, CancellationToken cancellationToken = default)
    {
        var queue = this._parkedEvents.GetOrAdd(parkedEvent.SubscriptionId, static _ => new ConcurrentQueue<ParkedEvent>());
        queue.Enqueue(parkedEvent);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<ParkedEvent>> GetParkedEventsAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        if (!this._parkedEvents.TryGetValue(subscriptionId, out var queue))
        {
            return Task.FromResult<IReadOnlyCollection<ParkedEvent>>(Array.Empty<ParkedEvent>());
        }

        var items = queue.ToArray().Where(x => !this._deleted.ContainsKey(x.ParkedEventId)).ToArray();
        return Task.FromResult<IReadOnlyCollection<ParkedEvent>>(items);
    }

    public Task RemoveParkedEventAsync(string subscriptionId, Guid parkedEventId, ISubscriptionConfigurationStore configurationStore, CancellationToken cancellationToken = default)
    {
        // Default to soft-delete when configuration is not available
        var softDelete = true;
        try
        {
            var subs = configurationStore.GetSubscriptionsAsync(cancellationToken).GetAwaiter().GetResult();
            var subscription = subs.FirstOrDefault(x => string.Equals(x.SubscriptionId, subscriptionId, StringComparison.OrdinalIgnoreCase));
            softDelete = subscription?.SoftDeleteParked ?? true;
        }
        catch
        {
            softDelete = true;
        }

        if (softDelete)
        {
            this._deleted[parkedEventId] = true;
            return Task.CompletedTask;
        }

        if (this._parkedEvents.TryGetValue(subscriptionId, out var queue))
        {
            var items = queue.ToArray().Where(x => x.ParkedEventId != parkedEventId).ToArray();
            var newQueue = new ConcurrentQueue<ParkedEvent>(items);
            this._parkedEvents[subscriptionId] = newQueue;
        }

        return Task.CompletedTask;
    }
}

public sealed class InMemoryReplaySessionStore : IReplaySessionStore
{
    private readonly ConcurrentDictionary<Guid, ReplaySession> _sessions = new();

    public Task<ReplaySession> StartAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        var session = new ReplaySession(Guid.NewGuid(), subscriptionId, DateTimeOffset.UtcNow, null);
        this._sessions[session.ReplaySessionId] = session;
        return Task.FromResult(session);
    }

    public Task CompleteAsync(Guid replaySessionId, CancellationToken cancellationToken = default)
    {
        if (this._sessions.TryGetValue(replaySessionId, out var session))
        {
            this._sessions[replaySessionId] = session with { CompletedAt = DateTimeOffset.UtcNow };
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<ReplaySession>> GetActiveSessionsAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        var activeSessions = this._sessions.Values
            .Where(x => x.SubscriptionId == subscriptionId && x.CompletedAt is null)
            .ToArray();

        return Task.FromResult<IReadOnlyCollection<ReplaySession>>(activeSessions);
    }
}

public sealed class InMemorySubscriptionEventSource : ISubscriptionEventSource
{
    private readonly ConcurrentDictionary<string, List<SubscriptionEvent>> _events = new();

    public InMemorySubscriptionEventSource(IEnumerable<SubscriptionEvent>? events = null)
    {
        foreach (var @event in events ?? Enumerable.Empty<SubscriptionEvent>())
        {
            this.Add(@event);
        }
    }

    public async Task SubscribeAsync(
        SubscriptionDefinition subscription,
        CheckpointState checkpoint,
        Func<SubscriptionEvent, CancellationToken, Task<bool>> eventAppeared,
        CancellationToken cancellationToken = default)
    {
        // If a specific secondary index is provided, subscribe to events for that index only.
        var afterCommitPosition = checkpoint.CommitPosition;
        var commitPosition = checkpoint.CommitPosition;

        if (!string.IsNullOrWhiteSpace(subscription.SecondaryIndexName))
        {
            if (this._events.TryGetValue(subscription.SecondaryIndexName, out var events))
            {
                var batch = events
                    // events in-memory may not have commit positions in tests; filter by commit position when available
                    .Where(x => (x.CommitPosition ?? long.MinValue) > (afterCommitPosition ?? long.MinValue))
                    .OrderBy(x => x.CommitPosition ?? long.MinValue)
                    .ToArray();

                foreach (var @event in batch)
                {
                    if (!await eventAppeared(@event, cancellationToken))
                    {
                        return;
                    }
                }
            }
        }
        else
        {
            // Catch-up subscription to all secondary indexes: merge events across all buckets and order by sequence number.
            var allEvents = new List<SubscriptionEvent>();
            foreach (var kvp in this._events)
            {
                var list = kvp.Value;
                lock (list)
                {
                    allEvents.AddRange(list);
                }
            }

            var batch = allEvents
                .Where(x => (x.CommitPosition ?? long.MinValue) > (afterCommitPosition ?? long.MinValue))
                .OrderBy(x => x.CommitPosition ?? long.MinValue)
                .ToArray();

            foreach (var @event in batch)
            {
                if (!await eventAppeared(@event, cancellationToken))
                {
                    return;
                }
            }
        }

        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    public async Task ReadFromBeginningAsync(
        SubscriptionDefinition subscription,
        Func<SubscriptionEvent, CancellationToken, Task> eventAppeared,
        CancellationToken cancellationToken = default)
    {
        var events = new List<SubscriptionEvent>();
        if (!string.IsNullOrWhiteSpace(subscription.SecondaryIndexName))
        {
            if (this._events.TryGetValue(subscription.SecondaryIndexName, out var indexedEvents))
            {
                lock (indexedEvents)
                {
                    events.AddRange(indexedEvents);
                }
            }
        }
        else
        {
            foreach (var indexedEvents in this._events.Values)
            {
                lock (indexedEvents)
                {
                    events.AddRange(indexedEvents);
                }
            }
        }

        foreach (var @event in events.OrderBy(x => x.CommitPosition ?? long.MinValue))
        {
            await eventAppeared(@event, cancellationToken);
        }
    }

    public async Task ReadFromCheckpointAsync(
        SubscriptionDefinition subscription,
        CheckpointState checkpoint,
        Func<SubscriptionEvent, CancellationToken, Task> eventAppeared,
        CancellationToken cancellationToken = default)
    {
        await this.ReadFromBeginningAsync(subscription, async (@event, ct) =>
        {
            if (checkpoint.CommitPosition is null || @event.CommitPosition > checkpoint.CommitPosition)
            {
                await eventAppeared(@event, ct);
            }
        }, cancellationToken);
    }

    public Task<IReadOnlyList<SubscriptionEvent>> PeekAsync(
        SubscriptionDefinition subscriptionDefinition,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        if (maxCount <= 0)
        {
            return Task.FromResult<IReadOnlyList<SubscriptionEvent>>(Array.Empty<SubscriptionEvent>());
        }

        var events = new List<SubscriptionEvent>();

        if (!string.IsNullOrWhiteSpace(subscriptionDefinition.SecondaryIndexName))
        {
            if (this._events.TryGetValue(subscriptionDefinition.SecondaryIndexName, out var indexedEvents))
            {
                lock (indexedEvents)
                {
                    events.AddRange(indexedEvents);
                }
            }
        }
        else
        {
            foreach (var kvp in this._events)
            {
                lock (kvp.Value)
                {
                    events.AddRange(kvp.Value);
                }
            }
        }

        var ordered = events
            .OrderBy(x => x.CommitPosition ?? long.MinValue)
            .Take(maxCount)
            .ToArray();

        return Task.FromResult<IReadOnlyList<SubscriptionEvent>>(ordered);
    }

    public void Add(SubscriptionEvent @event)
    {
        var list = this._events.GetOrAdd(@event.SecondaryIndexName, static _ => new List<SubscriptionEvent>());
        lock (list)
        {
            list.Add(@event);
        }
    }
}
