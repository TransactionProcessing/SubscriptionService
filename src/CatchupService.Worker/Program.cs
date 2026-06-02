using CatchupService.Infrastructure;
using CatchupService.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService();

builder.Services.AddCatchupService(builder.Configuration);
builder.Services.Configure<SubscriptionWorkerOptions>(builder.Configuration.GetSection("Subscriptions"));
builder.Services.AddHostedService<SubscriptionHostedService>();

var host = builder.Build();
await host.RunAsync();
