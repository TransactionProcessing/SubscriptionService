using SubscriptionService.Application;
using SubscriptionService.Infrastructure;

namespace SubscriptionService.Worker;

public sealed class BuiltInIndexDiscoveryClient(
    IConfiguration configuration,
    KurrentSqlBuiltInIndexClient flightSqlClient,
    LocalBuiltInIndexClient localClient,
    ILogger<BuiltInIndexDiscoveryClient> logger) : IEventStoreBuiltInIndexClient
{
    public Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
        this.ExecuteAsync(x => x.GetCategoriesAsync(cancellationToken), cancellationToken);

    public Task<IReadOnlyList<string>> GetEventTypesAsync(CancellationToken cancellationToken = default) =>
        this.ExecuteAsync(x => x.GetEventTypesAsync(cancellationToken), cancellationToken);

    private async Task<IReadOnlyList<string>> ExecuteAsync(
        Func<IEventStoreBuiltInIndexClient, Task<IReadOnlyList<string>>> operation,
        CancellationToken cancellationToken)
    {
        var mode = configuration["BuiltInIndexDiscovery:Mode"]?.Trim().ToLowerInvariant() ?? "auto";
        if (mode == "local")
        {
            return await operation(localClient);
        }

        if (mode == "flightsql")
        {
            return await operation(flightSqlClient);
        }

        try
        {
            var flightSqlTask = Task.Run(() => operation(flightSqlClient), cancellationToken);
            var completedTask = await Task.WhenAny(
                flightSqlTask,
                Task.Delay(TimeSpan.FromSeconds(2), cancellationToken));

            if (completedTask != flightSqlTask)
            {
                logger.LogDebug("Flight SQL built-in index discovery timed out; using local catalog");
                return await operation(localClient);
            }

            return await flightSqlTask;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(ex, "Flight SQL built-in index discovery unavailable; using local catalog");
            return await operation(localClient);
        }
    }
}
