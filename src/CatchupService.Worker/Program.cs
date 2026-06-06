using CatchupService.Application;
using CatchupService.Domain;
using CatchupService.Infrastructure;
using CatchupService.Infrastructure.Persistence;
using CatchupService.Worker;
using Google.Api;
using KurrentDB.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting.WindowsServices;

var builder = WebApplication.CreateBuilder(args);

IWebHostEnvironment env = builder.Environment;

builder.Configuration
    .AddJsonFile("hosting.json", optional: true)
    .AddJsonFile($"hosting.{env.EnvironmentName}.json", optional: true)
    .AddJsonFile("/home/txnproc/config/appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"/home/txnproc/config/appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

if (WindowsServiceHelpers.IsWindowsService())
{
    builder.Services.AddWindowsService();
}

var subscriptionDatabaseConnectionString = builder.Configuration.GetConnectionString("SubscriptionServiceDb")
    ?? throw new InvalidOperationException("Missing connection string 'SubscriptionServiceDb'.");
var connectionString = builder.Configuration.GetConnectionString("KurrentDb")
    ?? throw new InvalidOperationException("Missing connection string 'KurrentDb'.");
var statusEndpointPort = builder.Configuration.GetValue("SubscriptionService:StatusEndpointPort", 8080);

builder.Services.AddDbContextFactory<CatchupServiceDbContext>(options => options.UseSqlServer(subscriptionDatabaseConnectionString));
builder.Services.AddSingleton<ISubscriptionConfigurationStore, SqlSubscriptionConfigurationStore>();
builder.Services.AddSingleton<IDailyCommitPositionStore, SqlDailyCommitPositionStore>();
// If no SQL context available at registration time, fallback to in-memory - registration replaced in DI when SQL factory is available
builder.Services.AddSingleton<IIndexScanStateStore, SqlIndexScanStateStore>();
builder.Services.AddSingleton(new KurrentDBClient(KurrentDBClientSettings.Create(connectionString)));
builder.Services.AddSingleton<ISubscriptionEventSource, KurrentSubscriptionEventSource>();
builder.Services.AddSingleton<ICheckpointStore, SqlCheckpointStore>();
builder.Services.AddSingleton<IParkedEventStore, SqlParkedEventStore>();
builder.Services.AddSingleton<IReplaySessionStore, SqlReplaySessionStore>();
builder.Services.AddSingleton<IStreamEventCountStore, SqlStreamEventCountStore>();
builder.Services.AddSingleton<WorkerRuntimeRegistry>();
builder.Services.AddSingleton<ISubscriptionReplayService, SubscriptionReplayService>();
builder.Services.AddSingleton<RunningSubscriptionRegistry>();
builder.Services.AddSingleton<IRunningSubscriptionRegistry>(sp => sp.GetRequiredService<RunningSubscriptionRegistry>());
builder.Services.AddSingleton<ISubscriptionStatusService, SubscriptionStatusService>();
builder.Services.AddSingleton(
    new WorkerOptions(
        TimeSpan.FromSeconds(builder.Configuration.GetValue("SubscriptionService:ConfigurationPollIntervalSeconds", WorkerOptions.Default.ConfigurationPollInterval.TotalSeconds)),
        WorkerOptions.Default.SubscriptionResubscribeDelay));
builder.Services.AddHttpClient<IEventDeliveryClient, HttpEventDeliveryClient>();

builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<StreamEventCountService>();
builder.Services.AddHostedService<DailyCommitPositionService>();

var app = builder.Build();
app.Urls.Add($"http://localhost:{statusEndpointPort}");
app.MapSubscriptionStatusEndpoints();
app.MapSubscriptionConfigurationEndpoints();

using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CatchupServiceDbContext>>();
    await using var context = await factory.CreateDbContextAsync();
    await context.Database.MigrateAsync();
}

app.Run();
