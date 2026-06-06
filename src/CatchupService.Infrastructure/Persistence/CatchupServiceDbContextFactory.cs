using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CatchupService.Infrastructure.Persistence;

public sealed class CatchupServiceDbContextFactory : IDesignTimeDbContextFactory<CatchupServiceDbContext>
{
    public CatchupServiceDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("SubscriptionServiceDb")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__SubscriptionServiceDb")
            ?? "Server=localhost,1433;Database=CatchupService;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False";

        var optionsBuilder = new DbContextOptionsBuilder<CatchupServiceDbContext>();
        optionsBuilder.UseSqlServer(connectionString);
        return new CatchupServiceDbContext(optionsBuilder.Options);
    }
}
