using Apache.Arrow.Adbc.Client;
using Apache.Arrow.Adbc.Drivers.FlightSql;
using SubscriptionService.Application;

namespace SubscriptionService.Infrastructure;

public sealed class KurrentSqlBuiltInIndexClient : IEventStoreBuiltInIndexClient
{
    private readonly string _serverAddress;
    private readonly string? _username;
    private readonly string? _password;

    public KurrentSqlBuiltInIndexClient(string serverAddress, string? username, string? password)
    {
        this._serverAddress = serverAddress;
        this._username = username;
        this._password = password;
    }

    public Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
        this.ExecuteSingleColumnQueryAsync(
            "SELECT DISTINCT category FROM kdb.records WHERE stream NOT LIKE '$%' ORDER BY category",
            cancellationToken);

    public Task<IReadOnlyList<string>> GetEventTypesAsync(CancellationToken cancellationToken = default) =>
        this.ExecuteSingleColumnQueryAsync(
            "SELECT DISTINCT schema_name FROM kdb.records WHERE stream NOT LIKE '$%' ORDER BY schema_name",
            cancellationToken);

    private async Task<IReadOnlyList<string>> ExecuteSingleColumnQueryAsync(
        string sql,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var driver = new FlightSqlDriver();
        var parameters = new Dictionary<string, string>
        {
            [FlightSqlParameters.ServerAddress] = this._serverAddress
        };

        if (!string.IsNullOrWhiteSpace(this._username))
        {
            parameters["username"] = this._username;
        }

        if (!string.IsNullOrWhiteSpace(this._password))
        {
            parameters["password"] = this._password;
        }

        var options = new Dictionary<string, string>
        {
            [FlightSqlParameters.ServerAddress] = this._serverAddress
        };

        await using var connection = new AdbcConnection(driver, parameters, options);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();

        var values = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!reader.IsDBNull(0))
            {
                values.Add(reader.GetString(0));
            }
        }

        return values;
    }
}
