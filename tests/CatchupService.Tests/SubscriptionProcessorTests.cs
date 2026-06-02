using System.Collections.ObjectModel;
using CatchupService.Application;
using CatchupService.Contracts;
using CatchupService.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CatchupService.Tests;

public sealed class SubscriptionProcessorTests
{
    [Fact]
    public async Task RunAsync_AdvancesCheckpointOnSuccess_AndParksFailures()
    {
        var events = new[]
        {
            CreateEvent(1),
            CreateEvent(2)
        };

        var reader = new SequenceSubscriptionEventReader(events);
        var deliveryPipeline = new ScriptedDeliveryPipeline(DeliveryResult.Success(204), DeliveryResult.Failure(500, "boom"));
        var checkpointStore = new InMemoryCheckpointStore();
        var parkedStore = new InMemoryParkedEventStore();
        var processor = new SubscriptionProcessor(
            reader,
            deliveryPipeline,
            checkpointStore,
            parkedStore,
            Options.Create(new SubscriptionRuntimeOptions { MaxDeliveryAttempts = 1, RetryDelay = TimeSpan.Zero }),
            NullLogger<SubscriptionProcessor>.Instance);

        await processor.RunAsync(new SubscriptionDefinition("orders", new Uri("https://example.test/events"), QueueCapacity: 2, MaxDeliveryAttempts: 1), CancellationToken.None);

        var checkpoint = await checkpointStore.GetAsync("orders");
        Assert.NotNull(checkpoint);
        Assert.Equal(1, checkpoint!.Position);
        Assert.Single(parkedStore.Items);
        Assert.Equal(2, parkedStore.Items[0].Position);
    }

    private static SubscriptionEvent CreateEvent(long position) => new(
        "orders",
        position,
        new Uri("https://example.test/events"),
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>()),
        new byte[] { 1, 2, 3 },
        DateTimeOffset.UtcNow);

    private sealed class SequenceSubscriptionEventReader(IEnumerable<SubscriptionEvent> events) : ISubscriptionEventReader
    {
        public async IAsyncEnumerable<SubscriptionEvent> ReadAsync(SubscriptionDefinition subscription, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var item in events)
            {
                yield return item;
                await Task.Yield();
            }
        }
    }

    private sealed class ScriptedDeliveryPipeline(params DeliveryResult[] results) : IEventDeliveryPipeline
    {
        private readonly Queue<DeliveryResult> _results = new(results);

        public Task<DeliveryResult> DeliverAsync(SubscriptionEvent subscriptionEvent, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_results.Count > 0 ? _results.Dequeue() : DeliveryResult.Success(204));
        }
    }

    private sealed class InMemoryCheckpointStore : ICheckpointStore
    {
        private readonly Dictionary<string, SubscriptionCheckpoint> _checkpoints = new(StringComparer.OrdinalIgnoreCase);

        public Task<SubscriptionCheckpoint?> GetAsync(string subscriptionName, CancellationToken cancellationToken = default)
        {
            _checkpoints.TryGetValue(subscriptionName, out var checkpoint);
            return Task.FromResult<SubscriptionCheckpoint?>(checkpoint);
        }

        public Task SaveAsync(SubscriptionCheckpoint checkpoint, CancellationToken cancellationToken = default)
        {
            _checkpoints[checkpoint.SubscriptionName] = checkpoint;
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryParkedEventStore : IParkedEventStore
    {
        public List<ParkedSubscriptionEvent> Items { get; } = [];

        public Task StoreAsync(ParkedSubscriptionEvent parkedEvent, CancellationToken cancellationToken = default)
        {
            Items.Add(parkedEvent);
            return Task.CompletedTask;
        }
    }
}
