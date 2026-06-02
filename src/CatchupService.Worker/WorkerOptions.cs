namespace CatchupService.Worker;

public sealed record WorkerOptions(TimeSpan ConfigurationPollInterval, TimeSpan SubscriptionResubscribeDelay)
{
    public static WorkerOptions Default { get; } = new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
}
