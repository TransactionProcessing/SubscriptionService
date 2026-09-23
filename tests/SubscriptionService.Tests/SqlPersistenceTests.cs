using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SubscriptionService.Domain;
using SubscriptionService.Infrastructure.Persistence;

namespace SubscriptionService.Tests;

public sealed class SqlPersistenceTests
{
    [Fact]
    public async Task StoresRoundTripAcrossAllPersistenceTypes()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddDbContextFactory<CatchupServiceDbContext>(options => options.UseSqlite(connection));
        services.AddSingleton<ISubscriptionConfigurationStore, SqlSubscriptionConfigurationStore>();
        services.AddSingleton<ICheckpointStore, SqlCheckpointStore>();
        services.AddSingleton<IParkedEventStore, SqlParkedEventStore>();
        services.AddSingleton<IReplaySessionStore, SqlReplaySessionStore>();
        services.AddSingleton<ISubscriptionEventLogStore, SqlSubscriptionEventLogStore>();

        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CatchupServiceDbContext>>();
            await using var context = await factory.CreateDbContextAsync();
            await context.Database.EnsureCreatedAsync();
        }

        var subscriptionStore = provider.GetRequiredService<ISubscriptionConfigurationStore>();
        var checkpointStore = provider.GetRequiredService<ICheckpointStore>();
        var parkedStore = provider.GetRequiredService<IParkedEventStore>();
        var replayStore = provider.GetRequiredService<IReplaySessionStore>();

        var subscription = new SubscriptionDefinition(
            "sub-1",
            "index-1",
            "https://example.test/subscriptions/sub-1",
            "orders",
            new TimeoutSettings(TimeSpan.FromSeconds(10)),
            new RetrySettings(5, TimeSpan.FromSeconds(1)),
            new CheckpointSettings(50),
            false,
            new AuthenticationConfiguration("Bearer", new Dictionary<string, string> { ["token"] = "abc" }))
        {
            Enabled = false,
            OperationalState = SubscriptionOperationalState.Faulted,
            OperationalReason = "endpoint unreachable",
            EnableEventLogging = true
        };

        await subscriptionStore.UpsertAsync(subscription);

        var subscriptions = await subscriptionStore.GetSubscriptionsAsync();
        var storedSubscription = Assert.Single(subscriptions);
        Assert.Equal(subscription.SubscriptionId, storedSubscription.SubscriptionId);
        Assert.Equal("Bearer", storedSubscription.Authentication?.Scheme);
        Assert.Equal("abc", storedSubscription.Authentication?.Parameters["token"]);
        Assert.False(storedSubscription.Enabled);
        Assert.Equal(SubscriptionOperationalState.Faulted, storedSubscription.OperationalState);
        Assert.Equal("endpoint unreachable", storedSubscription.OperationalReason);
        Assert.True(storedSubscription.EnableEventLogging);

        await subscriptionStore.SetOperationalStateAsync(subscription.SubscriptionId, SubscriptionOperationalState.Healthy);
        storedSubscription = Assert.Single(await subscriptionStore.GetSubscriptionsAsync());
        Assert.True(storedSubscription.Enabled);
        Assert.Equal(SubscriptionOperationalState.Healthy, storedSubscription.OperationalState);
        Assert.Null(storedSubscription.OperationalReason);

        await checkpointStore.SaveCheckpointAsync(subscription.SubscriptionId, 42, 0, null, 17);
        var cp = await checkpointStore.GetCheckpointAsync(subscription.SubscriptionId);
        Assert.Equal(42, cp.CommitPosition);
        Assert.Equal(17, cp.PreparePosition);

        await checkpointStore.SaveCheckpointAsync(subscription.SubscriptionId, 43, 4, "batch-save", 18);
        cp = await checkpointStore.GetCheckpointAsync(subscription.SubscriptionId);
        Assert.Equal(43, cp.CommitPosition);
        Assert.Equal(18, cp.PreparePosition);
        Assert.Equal(4, cp.ProcessedCount);

        await checkpointStore.RemoveAsync(subscription.SubscriptionId);
        cp = await checkpointStore.GetCheckpointAsync(subscription.SubscriptionId);
        Assert.Null(cp.CommitPosition);
        Assert.Equal(0, cp.ProcessedCount);

        var parkedEvent = ParkedEvent.FromEvent(
            SubscriptionEvent.Create(
                "evt-1",
                subscription.SubscriptionId,
                subscription.SecondaryIndexName,
                "orders-99",
                "order.created",
                new byte[] { 1, 2, 3 },
                "application/json",
                new Dictionary<string, string> { ["customer-id"] = "c-123" }),
            "failed",
            3);

        await parkedStore.ParkAsync(parkedEvent);
        var storedParkedEvent = Assert.Single(await parkedStore.GetParkedEventsAsync(subscription.SubscriptionId));
        Assert.Equal("c-123", storedParkedEvent.Metadata["customer-id"]);
        Assert.Equal(3, storedParkedEvent.AttemptCount);

        var replaySession = await replayStore.StartAsync(subscription.SubscriptionId);
        Assert.Equal(subscription.SubscriptionId, replaySession.SubscriptionId);
        await replayStore.CompleteAsync(replaySession.ReplaySessionId);

        var eventLogStore = provider.GetRequiredService<ISubscriptionEventLogStore>();
        await eventLogStore.AddAsync(new SubscriptionEventLog(
            "evt-1", subscription.SubscriptionId, "payload", subscription.EndpointUrl, 202,
            "order.created", "accepted", DateTimeOffset.UtcNow, 2, true));
        await using var verifyContext = await provider.GetRequiredService<IDbContextFactory<CatchupServiceDbContext>>().CreateDbContextAsync();
        var storedLog = Assert.Single(await verifyContext.SubscriptionEventLogs.AsNoTracking().ToArrayAsync());
        Assert.Equal("evt-1", storedLog.EventId);
        Assert.Equal(subscription.SubscriptionId, storedLog.SubscriptionId);
        Assert.Equal("payload", storedLog.Payload);
        Assert.Equal(202, storedLog.ResponseStatusCode);
        Assert.Equal(2, storedLog.DeliveryAttempt);
        Assert.True(storedLog.IsReplay);
    }
}
