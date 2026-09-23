namespace SubscriptionService.Worker;

public sealed record WorkerOptions
{
    public TimeSpan ConfigurationPollInterval { get; init; }

    public TimeSpan SubscriptionResubscribeDelay { get; init; }

    public TimeSpan ReplayPauseTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public bool StartSubscriptionsOnStartup { get; init; } = true;

    public static WorkerOptions Default { get; } = new WorkerOptions
    {
        ConfigurationPollInterval = TimeSpan.FromSeconds(30),
        SubscriptionResubscribeDelay = TimeSpan.FromSeconds(5),
        ReplayPauseTimeout = TimeSpan.FromSeconds(30)
    };

    public WorkerOptions()
    {
    }

    public WorkerOptions(TimeSpan configurationPollInterval, TimeSpan subscriptionResubscribeDelay)
    {
        this.ConfigurationPollInterval = configurationPollInterval;
        this.SubscriptionResubscribeDelay = subscriptionResubscribeDelay;
    }
}
