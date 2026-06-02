using CatchupService.Application;
using CatchupService.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CatchupService.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCatchupService(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<StorageOptions>(configuration.GetSection("Storage"));
        services.Configure<SubscriptionRuntimeOptions>(configuration.GetSection("Runtime"));
        services.AddSingleton<SqliteConnectionFactory>();
        services.AddSingleton<ISubscriptionEventReader, NoopSubscriptionEventReader>();
        services.AddScoped<ICheckpointStore, SqliteCheckpointStore>();
        services.AddScoped<IParkedEventStore, SqliteParkedEventStore>();
        services.AddHttpClient<IEventDeliveryPipeline, HttpSubscriptionDeliveryPipeline>();
        services.AddScoped<SubscriptionProcessor>();
        return services;
    }
}
