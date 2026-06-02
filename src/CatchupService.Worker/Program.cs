using CatchupService.Application;
using CatchupService.Domain;
using CatchupService.Infrastructure;
using CatchupService.Infrastructure.Persistence;
using CatchupService.Worker;
using KurrentDB.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting.WindowsServices;

var builder = Host.CreateApplicationBuilder(args);

if (WindowsServiceHelpers.IsWindowsService())
{
    builder.Services.AddWindowsService();
}

var subscriptionDatabaseConnectionString = builder.Configuration.GetConnectionString("SubscriptionServiceDb")
    ?? throw new InvalidOperationException("Missing connection string 'SubscriptionServiceDb'.");
var connectionString = builder.Configuration.GetConnectionString("KurrentDb")
    ?? throw new InvalidOperationException("Missing connection string 'KurrentDb'.");
var deliveryClientMode = builder.Configuration.GetValue("SubscriptionService:DeliveryClient", "http");
builder.Services.AddDbContextFactory<CatchupServiceDbContext>(options => options.UseSqlServer(subscriptionDatabaseConnectionString));
builder.Services.AddSingleton<ISubscriptionConfigurationStore, SqlSubscriptionConfigurationStore>();
builder.Services.AddSingleton(new KurrentDBClient(KurrentDBClientSettings.Create(connectionString)));
builder.Services.AddSingleton<ISubscriptionEventSource, KurrentSubscriptionEventSource>();
builder.Services.AddSingleton<ICheckpointStore, SqlCheckpointStore>();
builder.Services.AddSingleton<IParkedEventStore, SqlParkedEventStore>();
builder.Services.AddSingleton<IReplaySessionStore, SqlReplaySessionStore>();
builder.Services.AddSingleton(
    new WorkerOptions(
        TimeSpan.FromSeconds(builder.Configuration.GetValue("SubscriptionService:ConfigurationPollIntervalSeconds", WorkerOptions.Default.ConfigurationPollInterval.TotalSeconds)),
        WorkerOptions.Default.SubscriptionResubscribeDelay));
if (string.Equals(deliveryClientMode, "console", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IEventDeliveryClient, ConsoleEventDeliveryClient>();
}
else
{
    builder.Services.AddHttpClient<IEventDeliveryClient, HttpEventDeliveryClient>();
}
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
using (var scope = host.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CatchupServiceDbContext>>();
    await using var context = await factory.CreateDbContextAsync();
    await context.Database.MigrateAsync();
}

host.Run();
