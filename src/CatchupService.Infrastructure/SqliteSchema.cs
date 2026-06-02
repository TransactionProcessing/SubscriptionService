using Microsoft.Data.Sqlite;

namespace CatchupService.Infrastructure;

internal static class SqliteSchema
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool initialized;

    public static async Task EnsureCreatedAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        if (initialized)
        {
            return;
        }

        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (initialized)
            {
                return;
            }

            using var checkpointCommand = connection.CreateCommand();
            checkpointCommand.CommandText = """
                CREATE TABLE IF NOT EXISTS SubscriptionCheckpoints (
                    SubscriptionName TEXT PRIMARY KEY,
                    Position INTEGER NOT NULL,
                    UpdatedAtUtc TEXT NOT NULL
                );
                """;
            await checkpointCommand.ExecuteNonQueryAsync(cancellationToken);

            using var parkedCommand = connection.CreateCommand();
            parkedCommand.CommandText = """
                CREATE TABLE IF NOT EXISTS ParkedEvents (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    SubscriptionName TEXT NOT NULL,
                    Position INTEGER NOT NULL,
                    Reason TEXT NOT NULL,
                    Payload BLOB NOT NULL,
                    HeadersJson TEXT NOT NULL,
                    ParkedAtUtc TEXT NOT NULL
                );
                """;
            await parkedCommand.ExecuteNonQueryAsync(cancellationToken);

            initialized = true;
        }
        finally
        {
            Gate.Release();
        }
    }
}
