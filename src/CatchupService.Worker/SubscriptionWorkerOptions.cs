using CatchupService.Core;

namespace CatchupService.Worker;

public sealed class SubscriptionWorkerOptions
{
    public List<SubscriptionDefinition> Subscriptions { get; set; } = [];
}
