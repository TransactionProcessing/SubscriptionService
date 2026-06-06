using CatchupService.Domain;
using System.Collections.Concurrent;

namespace CatchupService.Tests;

public sealed class WorkerRestartTests
{
    [Fact]
    public async Task WorkerLoop_StartsAndStopsWhenEnabledToggles()
    {
        var subs = new ConcurrentDictionary<string, SubscriptionDefinition>(StringComparer.OrdinalIgnoreCase);
        var initial = new SubscriptionDefinition(
            "sub-1",
            "index-1",
            "https://example.test/subscriptions/sub-1",
            "orders",
            new TimeoutSettings(TimeSpan.FromSeconds(5)),
            new RetrySettings(3, TimeSpan.Zero),
            new CheckpointSettings(10)) { Enabled = true };

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
        subs["sub-1"] = subs["sub-1"] with { Enabled = false };
        await WaitUntilAsync(() => stopped.ContainsKey("sub-1"), TimeSpan.FromSeconds(2));

        // Toggle enabled again
        subs["sub-1"] = subs["sub-1"] with { Enabled = true };
        await WaitUntilAsync(() => started["sub-1"] >= 2, TimeSpan.FromSeconds(2));

        cts.Cancel();
        await loop;

        Assert.True(started.ContainsKey("sub-1"));
        Assert.True(stopped.ContainsKey("sub-1"));
        Assert.True(started["sub-1"] >= 2);
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
            var desired = subscriptions.Where(x => x.Enabled).ToDictionary(x => x.SubscriptionId, StringComparer.OrdinalIgnoreCase);

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
}
