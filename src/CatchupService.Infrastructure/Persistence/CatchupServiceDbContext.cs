using Microsoft.EntityFrameworkCore;

namespace CatchupService.Infrastructure.Persistence;

public sealed class CatchupServiceDbContext(DbContextOptions<CatchupServiceDbContext> options) : DbContext(options)
{
    public DbSet<SubscriptionConfigurationEntity> SubscriptionConfigurations => Set<SubscriptionConfigurationEntity>();

    public DbSet<SubscriptionCheckpointEntity> SubscriptionCheckpoints => Set<SubscriptionCheckpointEntity>();

    public DbSet<ParkedEventEntity> ParkedEvents => Set<ParkedEventEntity>();

    public DbSet<ReplaySessionEntity> ReplaySessions => Set<ReplaySessionEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SubscriptionConfigurationEntity>(entity =>
        {
            entity.ToTable("SubscriptionConfigurations");
            entity.HasKey(x => x.SubscriptionId);
            entity.Property(x => x.SubscriptionId).HasMaxLength(200);
            entity.Property(x => x.SecondaryIndexName).HasMaxLength(200);
            entity.Property(x => x.EndpointUrl).HasMaxLength(2048);
            entity.Property(x => x.Tag).HasMaxLength(200);
            entity.Property(x => x.AuthenticationScheme).HasMaxLength(200);
        });

        modelBuilder.Entity<SubscriptionCheckpointEntity>(entity =>
        {
            entity.ToTable("SubscriptionCheckpoints");
            entity.HasKey(x => x.SubscriptionId);
            entity.Property(x => x.SubscriptionId).HasMaxLength(200);
        });

        modelBuilder.Entity<ParkedEventEntity>(entity =>
        {
            entity.ToTable("ParkedEvents");
            entity.HasKey(x => x.ParkedEventId);
            entity.Property(x => x.SubscriptionId).HasMaxLength(200);
            entity.Property(x => x.EventId).HasMaxLength(200);
            entity.Property(x => x.StreamName).HasMaxLength(512);
            entity.Property(x => x.EventType).HasMaxLength(200);
            entity.Property(x => x.FailureReason).HasMaxLength(4000);
            entity.Property(x => x.ContentType).HasMaxLength(200);
            entity.HasIndex(x => new { x.SubscriptionId, x.SequenceNumber });
        });

        modelBuilder.Entity<ReplaySessionEntity>(entity =>
        {
            entity.ToTable("ReplaySessions");
            entity.HasKey(x => x.ReplaySessionId);
            entity.Property(x => x.SubscriptionId).HasMaxLength(200);
            entity.HasIndex(x => x.SubscriptionId);
        });
    }
}
