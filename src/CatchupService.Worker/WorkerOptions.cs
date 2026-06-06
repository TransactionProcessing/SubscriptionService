namespace CatchupService.Worker;

public sealed record WorkerOptions
{
    public TimeSpan ConfigurationPollInterval { get; init; }

    public TimeSpan SubscriptionResubscribeDelay { get; init; }

    public static WorkerOptions Default { get; } = new WorkerOptions
    {
        ConfigurationPollInterval = TimeSpan.FromSeconds(30),
        SubscriptionResubscribeDelay = TimeSpan.FromSeconds(5)
    };

    public WorkerOptions()
    {
    }

    public WorkerOptions(TimeSpan configurationPollInterval, TimeSpan subscriptionResubscribeDelay)
    {
        ConfigurationPollInterval = configurationPollInterval;
        SubscriptionResubscribeDelay = subscriptionResubscribeDelay;
    }
}
