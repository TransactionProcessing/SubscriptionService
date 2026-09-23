using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SubscriptionService.Infrastructure.Persistence;

namespace SubscriptionService.Tests;

public sealed class SubscriptionEventLoggingMigrationTests
{
    [Fact]
    public void EventLoggingMigration_IsDiscoverableByEntityFramework()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        var options = new DbContextOptionsBuilder<CatchupServiceDbContext>()
            .UseSqlite(connection)
            .Options;

        using var context = new CatchupServiceDbContext(options);

        Assert.Contains(
            "20260914130000_subscription_event_logging",
            context.Database.GetMigrations());
    }
}
