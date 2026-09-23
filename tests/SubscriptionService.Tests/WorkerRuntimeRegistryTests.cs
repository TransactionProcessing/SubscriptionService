using SubscriptionService.Application;
using SubscriptionService.Worker;

namespace SubscriptionService.Tests;

public sealed class WorkerRuntimeRegistryTests
{
    [Fact]
    public async Task RequestStopAsync_completes_when_runtime_is_unregistered()
    {
        var registry = new WorkerRuntimeRegistry();
        using var cancellation = new CancellationTokenSource();
        var runtime = (SubscriptionRuntime)null!;
        var source = new CancellationTokenSource();
        registry.Register("sub-1", runtime, source);

        var stopTask = registry.RequestStopAsync("sub-1", TimeSpan.FromSeconds(1), cancellation.Token);
        registry.Unregister("sub-1");

        Assert.True(await stopTask);
        Assert.True(source.IsCancellationRequested);
    }

    [Fact]
    public async Task RequestStopAsync_returns_false_when_runtime_does_not_unregister()
    {
        var registry = new WorkerRuntimeRegistry();
        using var source = new CancellationTokenSource();
        registry.Register("sub-1", (SubscriptionRuntime)null!, source);

        var stopped = await registry.RequestStopAsync("sub-1", TimeSpan.FromMilliseconds(20));

        Assert.False(stopped);
        Assert.True(source.IsCancellationRequested);
    }
}
