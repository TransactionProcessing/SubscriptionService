using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using SubscriptionService.Domain;
using SubscriptionService.Worker;

namespace SubscriptionService.Tests;

public sealed class WorkerRestartTests
{
    [Fact]
    public async Task WorkerLoop_ContinuesPollingWhenConfigurationReadFails()
    {
        var configurationStore = new FailOnceConfigurationStore();
        var worker = new Worker.Worker(
            configurationStore,
            eventSource: null!,
            checkpointStore: null!,
            parkedEventStore: null!,
            replaySessionStore: null!,
            subscriptionEventLogStore:null,
            deliveryClient: null!,
            new WorkerRuntimeRegistry(),
            new RunningSubscriptionRegistry(),
            NullLoggerFactory.Instance,
            new WorkerOptions(TimeSpan.FromMilliseconds(10), TimeSpan.Zero),
            NullLogger<Worker.Worker>.Instance);

        await worker.StartAsync(CancellationToken.None);

        await WaitUntilAsync(
            () => configurationStore.ReadCount >= 2,
            TimeSpan.FromSeconds(2));

        await worker.StopAsync(CancellationToken.None);

        Assert.True(configurationStore.ReadCount >= 2);
    }

    [Fact]
    public async Task RestartTracker_DelaysFailedSubscriptionThenAllowsRestart()
    {
        var tracker = new SubscriptionRestartTracker(TimeSpan.FromMilliseconds(50));

        tracker.Schedule("sub-1");

        Assert.False(tracker.IsReady("sub-1"));
        await Task.Delay(100);
        Assert.True(tracker.IsReady("sub-1"));
        Assert.True(tracker.IsReady("sub-1"));
    }

    [Fact]
    public async Task WorkerLoop_StartsAndStopsWhenOperationalStateToggles()
    {
        var subs = new ConcurrentDictionary<string, SubscriptionDefinition>(StringComparer.OrdinalIgnoreCase);
        var initial = new SubscriptionDefinition(
            "sub-1",
            "index-1",
            "https://example.test/subscriptions/sub-1",
            "orders",
            new TimeoutSettings(TimeSpan.FromSeconds(5)),
            new RetrySettings(3, TimeSpan.Zero),
            new CheckpointSettings(10))
        {
            Enabled = true,
            OperationalState = SubscriptionOperationalState.Healthy
        };

        subs[initial.SubscriptionId] = initial;

        Task<IReadOnlyCollection<SubscriptionDefinition>> GetSubscriptions() => Task.FromResult((IReadOnlyCollection<SubscriptionDefinition>)subs.Values.ToArray());

        var started = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var stopped = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        Task StartSubscriptionAsync(SubscriptionDefinition s)
        {
            started.AddOrUpdate(s.SubscriptionId, 1, (_, v) => v + 1);
            return Task.CompletedTask;
        }

        Task StopSubscriptionAsync(string subscriptionId)
        {
            stopped.AddOrUpdate(subscriptionId, 1, (_, v) => v + 1);
            return Task.CompletedTask;
        }

        using var cts = new CancellationTokenSource();
        var loop = Task.Run(() => FakeWorkerLoop(GetSubscriptions, StartSubscriptionAsync, StopSubscriptionAsync, TimeSpan.FromMilliseconds(50), cts.Token));

        // Wait for the initial start
        await WaitUntilAsync(() => started.ContainsKey("sub-1"), TimeSpan.FromSeconds(2));

        // Toggle disabled
        subs["sub-1"] = subs["sub-1"] with { Enabled = false, OperationalState = SubscriptionOperationalState.Stopped };
        await WaitUntilAsync(() => stopped.ContainsKey("sub-1"), TimeSpan.FromSeconds(2));

        // Toggle enabled again
        subs["sub-1"] = subs["sub-1"] with { Enabled = true, OperationalState = SubscriptionOperationalState.Healthy };
        await WaitUntilAsync(() => started["sub-1"] >= 2, TimeSpan.FromSeconds(2));

        cts.Cancel();
        await loop;

        Assert.True(started.ContainsKey("sub-1"));
        Assert.True(stopped.ContainsKey("sub-1"));
        Assert.True(started["sub-1"] >= 2);
    }

    [Fact]
    public async Task WorkerLoop_DoesNotRestartFaultedSubscription()
    {
        var subs = new ConcurrentDictionary<string, SubscriptionDefinition>(StringComparer.OrdinalIgnoreCase);
        var initial = new SubscriptionDefinition(
            "sub-1",
            "index-1",
            "https://example.test/subscriptions/sub-1",
            "orders",
            new TimeoutSettings(TimeSpan.FromSeconds(5)),
            new RetrySettings(3, TimeSpan.Zero),
            new CheckpointSettings(10))
        {
            Enabled = true,
            OperationalState = SubscriptionOperationalState.Faulted,
            OperationalReason = "endpoint unreachable"
        };

        subs[initial.SubscriptionId] = initial;

        Task<IReadOnlyCollection<SubscriptionDefinition>> GetSubscriptions() => Task.FromResult((IReadOnlyCollection<SubscriptionDefinition>)subs.Values.ToArray());

        var started = 0;

        Task StartSubscriptionAsync(SubscriptionDefinition s)
        {
            started++;
            return Task.CompletedTask;
        }

        Task StopSubscriptionAsync(string subscriptionId) => Task.CompletedTask;

        using var cts = new CancellationTokenSource();
        var loop = Task.Run(() => FakeWorkerLoop(GetSubscriptions, StartSubscriptionAsync, StopSubscriptionAsync, TimeSpan.FromMilliseconds(50), cts.Token));

        await Task.Delay(250);

        cts.Cancel();
        await loop;

        Assert.Equal(0, started);
    }

    private static async Task FakeWorkerLoop(
        Func<Task<IReadOnlyCollection<SubscriptionDefinition>>> getSubscriptions,
        Func<SubscriptionDefinition, Task> start,
        Func<string, Task> stop,
        TimeSpan pollDelay,
        CancellationToken cancellationToken)
    {
        var running = new Dictionary<string, SubscriptionDefinition>(StringComparer.OrdinalIgnoreCase);

        while (!cancellationToken.IsCancellationRequested)
        {
            var subscriptions = await getSubscriptions();
            var desired = subscriptions
                .Where(x => x.Enabled && x.OperationalState == SubscriptionOperationalState.Healthy)
                .ToDictionary(x => x.SubscriptionId, StringComparer.OrdinalIgnoreCase);

            foreach (var (subscriptionId, current) in desired)
            {
                if (running.TryGetValue(subscriptionId, out var existing))
                {
                    if (!existing.Equals(current))
                    {
                        await stop(subscriptionId);
                        running.Remove(subscriptionId);
                        await start(current);
                        running[subscriptionId] = current;
                    }
                }
                else
                {
                    await start(current);
                    running[subscriptionId] = current;
                }
            }

            foreach (var subscriptionId in running.Keys.ToArray())
            {
                if (!desired.ContainsKey(subscriptionId))
                {
                    await stop(subscriptionId);
                    running.Remove(subscriptionId);
                }
            }

            try
            {
                await Task.Delay(pollDelay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (predicate()) return;
            await Task.Delay(20);
        }

        throw new TimeoutException("Condition not met within timeout");
    }

    private sealed class FailOnceConfigurationStore : ISubscriptionConfigurationStore
    {
        public int ReadCount { get; private set; }

        public Task<IReadOnlyCollection<SubscriptionDefinition>> GetSubscriptionsAsync(CancellationToken cancellationToken = default)
        {
            this.ReadCount++;
            if (this.ReadCount == 1)
            {
                throw new InvalidOperationException("transient configuration failure");
            }

            return Task.FromResult<IReadOnlyCollection<SubscriptionDefinition>>(Array.Empty<SubscriptionDefinition>());
        }

        public Task UpsertAsync(SubscriptionDefinition subscription, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SetOperationalStateAsync(
            string subscriptionId,
            SubscriptionOperationalState operationalState,
            string? operationalReason = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RemoveAsync(string subscriptionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
