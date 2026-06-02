using System.Text.Json;
using CatchupService.Contracts;
using CatchupService.Core;
using Microsoft.Data.Sqlite;

namespace CatchupService.Infrastructure;

public sealed class SqliteParkedEventStore(SqliteConnectionFactory connectionFactory) : IParkedEventStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task StoreAsync(ParkedSubscriptionEvent parkedEvent, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await SqliteSchema.EnsureCreatedAsync(connection, cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ParkedEvents (SubscriptionName, Position, Reason, Payload, HeadersJson, ParkedAtUtc)
            VALUES ($subscriptionName, $position, $reason, $payload, $headersJson, $parkedAtUtc);
            """;
        command.Parameters.AddWithValue("$subscriptionName", parkedEvent.SubscriptionName);
        command.Parameters.AddWithValue("$position", parkedEvent.Position);
        command.Parameters.AddWithValue("$reason", parkedEvent.Reason);
        command.Parameters.AddWithValue("$payload", parkedEvent.Payload);
        command.Parameters.AddWithValue("$headersJson", JsonSerializer.Serialize(parkedEvent.Headers, JsonOptions));
        command.Parameters.AddWithValue("$parkedAtUtc", parkedEvent.ParkedAtUtc.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
