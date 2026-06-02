namespace CatchupService.Worker;

public sealed record WorkerOptions(TimeSpan ConfigurationPollInterval)
{
    public static WorkerOptions Default { get; } = new(TimeSpan.FromSeconds(5));
}
