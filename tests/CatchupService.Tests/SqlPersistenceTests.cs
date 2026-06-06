using CatchupService.Domain;
using CatchupService.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CatchupService.Tests;

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
        { Enabled = false };

        await subscriptionStore.UpsertAsync(subscription);

        var subscriptions = await subscriptionStore.GetSubscriptionsAsync();
        var storedSubscription = Assert.Single(subscriptions);
        Assert.Equal(subscription.SubscriptionId, storedSubscription.SubscriptionId);
        Assert.Equal("Bearer", storedSubscription.Authentication?.Scheme);
        Assert.Equal("abc", storedSubscription.Authentication?.Parameters["token"]);
        Assert.False(storedSubscription.Enabled);

        await checkpointStore.SaveCheckpointAsync(subscription.SubscriptionId, 42, 0, null);
        var cp = await checkpointStore.GetCheckpointAsync(subscription.SubscriptionId);
        Assert.Equal(42, cp.CommitPosition);

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
    }
}
