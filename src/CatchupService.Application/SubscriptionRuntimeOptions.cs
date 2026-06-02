namespace CatchupService.Application;

public sealed class SubscriptionRuntimeOptions
{
    public int DefaultQueueCapacity { get; set; } = 256;

    public int MaxDeliveryAttempts { get; set; } = 3;

    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(2);
}
