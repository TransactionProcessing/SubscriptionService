using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace CatchupService.Infrastructure;

public sealed class SqliteConnectionFactory(IOptions<StorageOptions> options)
{
    public SqliteConnection Create() => new(options.Value.ConnectionString);
}
