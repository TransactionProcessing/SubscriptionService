namespace CatchupService.Infrastructure;

public sealed class StorageOptions
{
    public string ConnectionString { get; set; } = "Data Source=catchupservice.db";
}
