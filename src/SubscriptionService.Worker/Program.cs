using System.Net.Http.Headers;
using System.Text;
using SubscriptionService.Worker.Components;
using KurrentDB.Client;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting.WindowsServices;
using NLog.Web;
using SubscriptionService.Application;
using SubscriptionService.Domain;
using SubscriptionService.Infrastructure;
using SubscriptionService.Infrastructure.Persistence;
using SubscriptionService.Worker;
using SubscriptionService.Worker.Ui;

var builder = WebApplication.CreateBuilder(args);

var isWindowsService = WindowsServiceHelpers.IsWindowsService();
if (isWindowsService)
{
    var executableDirectory = AppContext.BaseDirectory;
    builder.WebHost.UseContentRoot(executableDirectory);
    builder.WebHost.UseWebRoot(WebHostPathResolver.ResolveWebRoot(
        isWindowsService,
        builder.Environment.ContentRootPath,
        executableDirectory));
}

builder.Logging.ClearProviders();
builder.Host.UseNLog();

IWebHostEnvironment env = builder.Environment;

builder.Configuration
    .AddJsonFile("/home/txnproc/config/appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"/home/txnproc/config/appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddJsonFile("hosting.json", optional: true)
    .AddJsonFile($"hosting.{env.EnvironmentName}.json", optional: true)
    .AddJsonFile($"/home/txnproc/config/appsettings.local.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

StaticWebAssetsLoader.UseStaticWebAssets(builder.Environment, builder.Configuration);

var configuredDataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
var dataProtectionKeysCandidates = string.IsNullOrWhiteSpace(configuredDataProtectionKeysPath)
    ? new[]
    {
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Vme",
            "CatchupSubscriptionService",
            "DataProtection-Keys"),
        Path.Combine(builder.Environment.ContentRootPath, "DataProtection-Keys")
    }
    : new[] { configuredDataProtectionKeysPath };

var dataProtectionKeysPath = dataProtectionKeysCandidates.FirstOrDefault(path => TryCreateDirectory(path));
if (dataProtectionKeysPath is null)
{
    throw new InvalidOperationException(
        $"Unable to create a writable Data Protection key directory. Configure DataProtection:KeysPath. Tried: {string.Join(", ", dataProtectionKeysCandidates)}");
}

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
    .SetApplicationName("Vme.CatchupSubscriptionService");

if (isWindowsService)
{
    builder.Services.AddWindowsService();
}

var subscriptionDatabaseConnectionString = builder.Configuration.GetConnectionString("SubscriptionServiceDb")
    ?? throw new InvalidOperationException("Missing connection string 'SubscriptionServiceDb'.");
var connectionString = builder.Configuration.GetConnectionString("KurrentDb")
    ?? throw new InvalidOperationException("Missing connection string 'KurrentDb'.");
var kurrentClientSettings = KurrentDBClientSettings.Create(connectionString);
var kurrentAddress = kurrentClientSettings.ConnectivitySettings.Address
    ?? throw new InvalidOperationException("KurrentDB connection address is missing.");
var kurrentSqlAddress = kurrentAddress.Scheme switch
{
    "http" => new UriBuilder(kurrentAddress) { Scheme = Uri.UriSchemeHttp }.Uri.ToString().TrimEnd('/'),
    "https" => new UriBuilder(kurrentAddress) { Scheme = Uri.UriSchemeHttps }.Uri.ToString().TrimEnd('/'),
    _ => kurrentAddress.ToString().TrimEnd('/')
};

builder.Services.AddDbContextFactory<CatchupServiceDbContext>(options => options.UseSqlServer(subscriptionDatabaseConnectionString));
builder.Services.AddSingleton<ISubscriptionConfigurationStore, SqlSubscriptionConfigurationStore>();
builder.Services.AddSingleton<ISubscriptionEventLogStore, SqlSubscriptionEventLogStore>();
builder.Services.AddSingleton<IEndpointStore, SqlEndpointStore>();
builder.Services.AddSingleton<IDailyCommitPositionStore, SqlDailyCommitPositionStore>();
builder.Services.AddSingleton<DailyCommitPositionPlanner>();
builder.Services.AddSingleton<DailyCommitPositionBackfillService>();
// If no SQL context available at registration time, fallback to in-memory - registration replaced in DI when SQL factory is available
builder.Services.AddSingleton<IIndexScanStateStore, SqlIndexScanStateStore>();
builder.Services.AddSingleton<IBuiltInIndexCatalogStore, SqlBuiltInIndexCatalogStore>();
builder.Services.AddSingleton<LocalBuiltInIndexClient>();
builder.Services.AddHostedService<BuiltInIndexCatalogWorker>();
builder.Services.AddSingleton(new KurrentDBClient(kurrentClientSettings));
builder.Services.AddSingleton<KurrentSqlBuiltInIndexClient>(_ => new KurrentSqlBuiltInIndexClient(
    kurrentSqlAddress,
    kurrentClientSettings.DefaultCredentials?.Username,
    kurrentClientSettings.DefaultCredentials?.Password));
builder.Services.AddSingleton<IEventStoreBuiltInIndexClient, BuiltInIndexDiscoveryClient>();
builder.Services.AddHttpClient<IEventStoreIndexClient, EventStoreIndexClient>(client =>
{
    client.BaseAddress = kurrentClientSettings.ConnectivitySettings.Address;
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

    if (kurrentClientSettings.DefaultCredentials is { } credentials)
    {
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credentials.Username}:{credentials.Password}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
    }
})
    .ConfigurePrimaryHttpMessageHandler(() => kurrentClientSettings.CreateHttpMessageHandler?.Invoke() ?? new HttpClientHandler());
builder.Services.AddSingleton<ISubscriptionEventSource, KurrentSubscriptionEventSource>();
builder.Services.AddSingleton<ICheckpointStore, SqlCheckpointStore>();
builder.Services.AddSingleton<IParkedEventStore, SqlParkedEventStore>();
builder.Services.AddSingleton<IReplaySessionStore, SqlReplaySessionStore>();
builder.Services.AddSingleton<IStreamEventCountStore, SqlStreamEventCountStore>();
builder.Services.AddSingleton<IIndexEventReader, KurrentIndexEventReader>();
builder.Services.AddSingleton<WorkerRuntimeRegistry>();
builder.Services.AddSingleton<CountConnectionRegistry>();
builder.Services.AddSingleton<CountWorkerStatusRegistry>();
builder.Services.AddSingleton<ISubscriptionReplayService, SubscriptionReplayService>();
builder.Services.AddSingleton<RunningSubscriptionRegistry>();
builder.Services.AddSingleton<IRunningSubscriptionRegistry>(sp => sp.GetRequiredService<RunningSubscriptionRegistry>());
builder.Services.AddSingleton<ISubscriptionStatusService, SubscriptionStatusService>();
builder.Services.AddSingleton(
    new WorkerOptions(
        TimeSpan.FromSeconds(builder.Configuration.GetValue("SubscriptionService:ConfigurationPollIntervalSeconds", WorkerOptions.Default.ConfigurationPollInterval.TotalSeconds)),
        WorkerOptions.Default.SubscriptionResubscribeDelay)
    {
        ReplayPauseTimeout = TimeSpan.FromSeconds(builder.Configuration.GetValue(
            "SubscriptionService:ReplayPauseTimeoutSeconds",
            WorkerOptions.Default.ReplayPauseTimeout.TotalSeconds)),
        StartSubscriptionsOnStartup = builder.Configuration.GetValue(
            "SubscriptionService:StartSubscriptionsOnStartup",
            WorkerOptions.Default.StartSubscriptionsOnStartup)
    });
builder.Services.AddHttpClient<IEventDeliveryClient, HttpEventDeliveryClient>();
builder.Services.AddHttpClient<ICatchupServiceClient, CatchupServiceClient>(client =>
{
    var baseUrl = builder.Configuration["CatchupService:BaseUrl"]
        ?? builder.Configuration["Urls"]?.Split(';', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
        ?? "http://localhost:8080";
    client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute);
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
});
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSingleton<SubscriptionAdminService>();

var startSubscriptionsOnStartup = builder.Configuration.GetValue(
    "SubscriptionService:StartSubscriptionsOnStartup",
    WorkerOptions.Default.StartSubscriptionsOnStartup);

if (startSubscriptionsOnStartup)
{
    builder.Services.AddHostedService<Worker>();
    builder.Services.AddHostedService<StreamEventCountService>();
    builder.Services.AddHostedService<DailyCommitPositionService>();
}

var app = builder.Build();
SubscriptionLogger.Initialize(app.Services.GetRequiredService<ILoggerFactory>());
app.UseStaticFiles();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapEventStoreIndexEndpoints();
app.MapSubscriptionStatusEndpoints();
app.MapSubscriptionConfigurationEndpoints();
app.MapEndpointEndpoints();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CatchupServiceDbContext>>();
    await using var context = await factory.CreateDbContextAsync();
    await context.Database.MigrateAsync();
}

app.Run();

static bool TryCreateDirectory(string path)
{
    try
    {
        Directory.CreateDirectory(path);
        return true;
    }
    catch (UnauthorizedAccessException)
    {
        return false;
    }
    catch (IOException)
    {
        return false;
    }
}
