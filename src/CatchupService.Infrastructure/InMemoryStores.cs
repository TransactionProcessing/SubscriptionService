using System.Collections.Concurrent;
using CatchupService.Domain;

namespace CatchupService.Infrastructure;

public sealed class InMemorySubscriptionConfigurationStore : ISubscriptionConfigurationStore
{
    private readonly ConcurrentDictionary<string, SubscriptionDefinition> _subscriptions = new();

    public InMemorySubscriptionConfigurationStore(IEnumerable<SubscriptionDefinition>? subscriptions = null)
    {
        foreach (var subscription in subscriptions ?? Enumerable.Empty<SubscriptionDefinition>())
        {
            _subscriptions[subscription.SubscriptionId] = subscription;
        }
    }

    public Task<IReadOnlyCollection<SubscriptionDefinition>> GetSubscriptionsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyCollection<SubscriptionDefinition>>(_subscriptions.Values.OrderBy(x => x.SubscriptionId).ToArray());

    public Task UpsertAsync(SubscriptionDefinition subscription, CancellationToken cancellationToken = default)
    {
        _subscriptions[subscription.SubscriptionId] = subscription;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        _subscriptions.TryRemove(subscriptionId, out _);
        return Task.CompletedTask;
    }

    public void Upsert(SubscriptionDefinition subscription) => _subscriptions[subscription.SubscriptionId] = subscription;

    public void Remove(string subscriptionId) => _subscriptions.TryRemove(subscriptionId, out _);
}

public sealed class InMemoryCheckpointStore : ICheckpointStore
{
    private readonly ConcurrentDictionary<string, long?> _commitPositions = new();
    private readonly ConcurrentDictionary<string, long> _processedCounts = new();

    public Task<CheckpointState> GetCheckpointAsync(string subscriptionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new CheckpointState(
            _commitPositions.TryGetValue(subscriptionId, out var commitPos) && commitPos.HasValue ? commitPos : 0L,
            _processedCounts.TryGetValue(subscriptionId, out var pc) ? pc : 0L,
            null));

    public Task SaveCheckpointAsync(string subscriptionId, long? commitPosition, long processedCount, string? checkpointReason = null, CancellationToken cancellationToken = default)
    {
        _commitPositions[subscriptionId] = commitPosition;
        _processedCounts[subscriptionId] = processedCount;
        return Task.CompletedTask;
    }
}

public sealed class InMemoryParkedEventStore : IParkedEventStore
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<ParkedEvent>> _parkedEvents = new();
    private readonly ConcurrentDictionary<Guid, bool> _deleted = new();

    public Task ParkAsync(ParkedEvent parkedEvent, CancellationToken cancellationToken = default)
    {
        var queue = _parkedEvents.GetOrAdd(parkedEvent.SubscriptionId, static _ => new ConcurrentQueue<ParkedEvent>());
        queue.Enqueue(parkedEvent);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<ParkedEvent>> GetParkedEventsAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        if (!_parkedEvents.TryGetValue(subscriptionId, out var queue))
        {
            return Task.FromResult<IReadOnlyCollection<ParkedEvent>>(Array.Empty<ParkedEvent>());
        }

        var items = queue.ToArray().Where(x => !_deleted.ContainsKey(x.ParkedEventId)).ToArray();
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
            _deleted[parkedEventId] = true;
            return Task.CompletedTask;
        }

        if (_parkedEvents.TryGetValue(subscriptionId, out var queue))
        {
            var items = queue.ToArray().Where(x => x.ParkedEventId != parkedEventId).ToArray();
            var newQueue = new ConcurrentQueue<ParkedEvent>(items);
            _parkedEvents[subscriptionId] = newQueue;
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
        _sessions[session.ReplaySessionId] = session;
        return Task.FromResult(session);
    }

    public Task CompleteAsync(Guid replaySessionId, CancellationToken cancellationToken = default)
    {
        if (_sessions.TryGetValue(replaySessionId, out var session))
        {
            _sessions[replaySessionId] = session with { CompletedAt = DateTimeOffset.UtcNow };
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<ReplaySession>> GetActiveSessionsAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        var activeSessions = _sessions.Values
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
            Add(@event);
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
            if (_events.TryGetValue(subscription.SecondaryIndexName, out var events))
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
            foreach (var kvp in _events)
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

    public void Add(SubscriptionEvent @event)
    {
        var list = _events.GetOrAdd(@event.SecondaryIndexName, static _ => new List<SubscriptionEvent>());
        lock (list)
        {
            list.Add(@event);
        }
    }
}
