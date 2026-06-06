using CatchupService.Worker;
using CatchupService.Domain;
using CatchupService.Tests.Integration;
using Microsoft.Extensions.Logging.Abstractions;

namespace CatchupService.Tests.Integration;

public sealed class WorkerIntegrationTests
{
    [Fact]
    public async Task Worker_StartsAndStopsSubscription_WhenEnabledToggles()
    {
        var subs = new Dictionary<string, SubscriptionDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["sub-1"] = new SubscriptionDefinition(
                "sub-1",
                "index-1",
                "https://example.test/subscriptions/sub-1",
                "orders",
                new TimeoutSettings(TimeSpan.FromSeconds(5)),
                new RetrySettings(3, TimeSpan.Zero),
                new CheckpointSettings(10)) { Enabled = true }
        };

        Task<IReadOnlyCollection<SubscriptionDefinition>> GetSubscriptions(CancellationToken ct) => Task.FromResult((IReadOnlyCollection<SubscriptionDefinition>)subs.Values.ToArray());

        var configurationStore = new SimpleInMemorySubscriptionConfigurationStore(GetSubscriptions);
        var eventSource = new NoopEventSource();
        var checkpointStore = new NoopCheckpointStore();
        var parkedStore = new NoopParkedEventStore();
        var replayStore = new NoopReplaySessionStore();
        var deliveryClient = new NoopDeliveryClient();
        var runtimeRegistry = new WorkerRuntimeRegistry();
        var runningRegistry = new RunningSubscriptionRegistry();

        var options = new CatchupService.Worker.WorkerOptions(
            TimeSpan.FromMilliseconds(100),
            CatchupService.Worker.WorkerOptions.Default.SubscriptionResubscribeDelay);

        var worker = new CatchupService.Worker.Worker(
            configurationStore,
            eventSource,
            checkpointStore,
            parkedStore,
            replayStore,
            deliveryClient,
            runtimeRegistry,
            runningRegistry,
            NullLoggerFactory.Instance,
            options,
            NullLogger<CatchupService.Worker.Worker>.Instance);

        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);

        // wait for the worker to pick up the enabled subscription
        await WaitUntilAsync(() => runningRegistry.IsRunning("sub-1"), TimeSpan.FromSeconds(10));

        // disable subscription
        subs["sub-1"] = subs["sub-1"] with { Enabled = false };

        // wait for worker to stop it
        await WaitUntilAsync(() => !runningRegistry.IsRunning("sub-1"), TimeSpan.FromSeconds(10));

        // re-enable
        subs["sub-1"] = subs["sub-1"] with { Enabled = true };

        // wait for it to start again
        await WaitUntilAsync(() => runningRegistry.IsRunning("sub-1"), TimeSpan.FromSeconds(10));

        await worker.StopAsync(CancellationToken.None);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (predicate()) return;
            await Task.Delay(50);
        }

        throw new TimeoutException("Condition not met within timeout");
    }
}
