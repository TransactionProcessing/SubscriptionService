namespace CatchupService.Application;

public interface IStreamEventCountStore
{
    /// <summary>
    /// Get the total number of events recorded for a secondary index, or null if unknown.
    /// </summary>
    Task<long?> GetTotalForIndexAsync(string secondaryIndexName, CancellationToken cancellationToken = default);
}
