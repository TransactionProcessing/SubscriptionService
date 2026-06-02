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

    public void Upsert(SubscriptionDefinition subscription) => _subscriptions[subscription.SubscriptionId] = subscription;

    public void Remove(string subscriptionId) => _subscriptions.TryRemove(subscriptionId, out _);
}

public sealed class InMemoryCheckpointStore : ICheckpointStore
{
    private readonly ConcurrentDictionary<string, long> _checkpoints = new();

    public Task<long> GetCheckpointAsync(string subscriptionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_checkpoints.TryGetValue(subscriptionId, out var checkpoint) ? checkpoint : 0L);

    public Task SaveCheckpointAsync(string subscriptionId, long checkpoint, CancellationToken cancellationToken = default)
    {
        _checkpoints[subscriptionId] = checkpoint;
        return Task.CompletedTask;
    }
}

public sealed class InMemoryParkedEventStore : IParkedEventStore
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<ParkedEvent>> _parkedEvents = new();

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

        return Task.FromResult<IReadOnlyCollection<ParkedEvent>>(queue.ToArray());
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
        long afterSequenceNumber,
        Func<SubscriptionEvent, CancellationToken, Task<bool>> eventAppeared,
        CancellationToken cancellationToken = default)
    {
        if (_events.TryGetValue(subscription.SecondaryIndexName, out var events))
        {
            var batch = events
                .Where(x => x.SequenceNumber > afterSequenceNumber)
                .OrderBy(x => x.SequenceNumber)
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
