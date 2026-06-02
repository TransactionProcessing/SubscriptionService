using CatchupService.Application;
using CatchupService.Domain;
using CatchupService.Infrastructure;
using CatchupService.Worker;
using Microsoft.Extensions.Hosting.WindowsServices;

var builder = Host.CreateApplicationBuilder(args);

if (WindowsServiceHelpers.IsWindowsService())
{
    builder.Services.AddWindowsService();
}

builder.Services.AddSingleton<ISubscriptionConfigurationStore>(_ => new InMemorySubscriptionConfigurationStore());
builder.Services.AddSingleton<ISubscriptionEventSource>(_ => new InMemorySubscriptionEventSource());
builder.Services.AddSingleton<ICheckpointStore, InMemoryCheckpointStore>();
builder.Services.AddSingleton<IParkedEventStore, InMemoryParkedEventStore>();
builder.Services.AddSingleton<IReplaySessionStore, InMemoryReplaySessionStore>();
builder.Services.AddHttpClient<IEventDeliveryClient, HttpEventDeliveryClient>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
