namespace CatchupService.Application;

public interface IRunningSubscriptionRegistry
{
    bool IsRunning(string subscriptionId);
}