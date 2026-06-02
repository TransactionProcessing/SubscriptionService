using CatchupService.Core;
using Microsoft.Data.Sqlite;

namespace CatchupService.Infrastructure;

public sealed class SqliteCheckpointStore(SqliteConnectionFactory connectionFactory) : ICheckpointStore
{
    public async Task<SubscriptionCheckpoint?> GetAsync(string subscriptionName, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await SqliteSchema.EnsureCreatedAsync(connection, cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Position, UpdatedAtUtc FROM SubscriptionCheckpoints WHERE SubscriptionName = $subscriptionName";
        command.Parameters.AddWithValue("$subscriptionName", subscriptionName);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new SubscriptionCheckpoint(
            subscriptionName,
            reader.GetInt64(0),
            DateTimeOffset.Parse(reader.GetString(1)));
    }

    public async Task SaveAsync(SubscriptionCheckpoint checkpoint, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await SqliteSchema.EnsureCreatedAsync(connection, cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO SubscriptionCheckpoints (SubscriptionName, Position, UpdatedAtUtc)
            VALUES ($subscriptionName, $position, $updatedAtUtc)
            ON CONFLICT(SubscriptionName) DO UPDATE SET
                Position = excluded.Position,
                UpdatedAtUtc = excluded.UpdatedAtUtc;
            """;
        command.Parameters.AddWithValue("$subscriptionName", checkpoint.SubscriptionName);
        command.Parameters.AddWithValue("$position", checkpoint.Position);
        command.Parameters.AddWithValue("$updatedAtUtc", checkpoint.UpdatedAtUtc.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
