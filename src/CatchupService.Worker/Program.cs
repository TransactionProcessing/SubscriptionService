using CatchupService.Application;
using CatchupService.Domain;
using CatchupService.Infrastructure;
using CatchupService.Worker;
using KurrentDB.Client;
using Microsoft.Extensions.Hosting.WindowsServices;

var builder = Host.CreateApplicationBuilder(args);

if (WindowsServiceHelpers.IsWindowsService())
{
    builder.Services.AddWindowsService();
}

builder.Services.AddSingleton<ISubscriptionConfigurationStore>(_ => new InMemorySubscriptionConfigurationStore());
var connectionString = builder.Configuration.GetConnectionString("KurrentDb")
    ?? throw new InvalidOperationException("Missing connection string 'KurrentDb'.");
builder.Services.AddSingleton(new KurrentDBClient(KurrentDBClientSettings.Create(connectionString)));
builder.Services.AddSingleton<ISubscriptionEventSource, KurrentSubscriptionEventSource>();
builder.Services.AddSingleton<ICheckpointStore, InMemoryCheckpointStore>();
builder.Services.AddSingleton<IParkedEventStore, InMemoryParkedEventStore>();
builder.Services.AddSingleton<IReplaySessionStore, InMemoryReplaySessionStore>();
builder.Services.AddSingleton(new WorkerOptions(TimeSpan.FromSeconds(builder.Configuration.GetValue("SubscriptionService:ConfigurationPollIntervalSeconds", WorkerOptions.Default.ConfigurationPollInterval.TotalSeconds))));
builder.Services.AddHttpClient<IEventDeliveryClient, HttpEventDeliveryClient>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
