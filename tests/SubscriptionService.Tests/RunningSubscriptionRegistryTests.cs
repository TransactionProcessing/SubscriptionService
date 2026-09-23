using SubscriptionService.Worker;

namespace SubscriptionService.Tests;

public sealed class RunningSubscriptionRegistryTests
{
    [Fact]
    public void Registry_records_lifecycle_state_for_diagnosis()
    {
        var registry = new RunningSubscriptionRegistry();

        registry.MarkRunning("sub-1");
        registry.MarkStopped("sub-1", "EventStore subscription completed");

        var state = registry.GetState("sub-1");

        Assert.NotNull(state);
        Assert.NotNull(state!.LastStartedAt);
        Assert.NotNull(state.LastStoppedAt);
        Assert.Equal("EventStore subscription completed", state.LastStopReason);
        Assert.Equal(1, state.StopCount);
    }

    [Fact]
    public void Registry_records_failure_as_last_stop_reason()
    {
        var registry = new RunningSubscriptionRegistry();

        registry.MarkRunning("sub-1");
        registry.MarkFailed("sub-1", new InvalidOperationException("connection lost"));

        var state = registry.GetState("sub-1");

        Assert.NotNull(state);
        Assert.Equal("connection lost", state!.LastStopReason);
        Assert.Equal(1, state.StopCount);
    }
}
