using Microsoft.EntityFrameworkCore;

namespace CatchupService.Infrastructure.Persistence;

public sealed class CatchupServiceDbContext(DbContextOptions<CatchupServiceDbContext> options) : DbContext(options)
{
    public DbSet<SubscriptionConfigurationEntity> SubscriptionConfigurations => Set<SubscriptionConfigurationEntity>();

    public DbSet<SubscriptionCheckpointEntity> SubscriptionCheckpoints => Set<SubscriptionCheckpointEntity>();

    public DbSet<ParkedEventEntity> ParkedEvents => Set<ParkedEventEntity>();

    public DbSet<ReplaySessionEntity> ReplaySessions => Set<ReplaySessionEntity>();

    public DbSet<StreamEventCountEntity> StreamEventCounts => Set<StreamEventCountEntity>();
    public DbSet<DailyCommitPositionEntity> DailyCommitPositions => Set<DailyCommitPositionEntity>();
    public DbSet<IndexScanStateEntity> IndexScanStates => Set<IndexScanStateEntity>();

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
            entity.Property(x => x.ContinueOnParked);
            entity.Property(x => x.Enabled);
            entity.Property(x => x.SoftDeleteParked).HasDefaultValue(true);
        });

        modelBuilder.Entity<SubscriptionCheckpointEntity>(entity =>
        {
            entity.ToTable("SubscriptionCheckpoints");
            entity.HasKey(x => x.SubscriptionId);
            entity.Property(x => x.SubscriptionId).HasMaxLength(200);
            entity.Property(x => x.CommitPosition);
            entity.Property(x => x.ProcessedCount).HasDefaultValue(0);
            entity.Property(x => x.CheckpointReason).HasMaxLength(200);
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
            entity.Property(x => x.IsDeleted).HasDefaultValue(false);
        });

        modelBuilder.Entity<ReplaySessionEntity>(entity =>
        {
            entity.ToTable("ReplaySessions");
            entity.HasKey(x => x.ReplaySessionId);
            entity.Property(x => x.SubscriptionId).HasMaxLength(200);
            entity.HasIndex(x => x.SubscriptionId);
        });

        modelBuilder.Entity<StreamEventCountEntity>(entity =>
        {
            entity.ToTable("StreamEventCounts");
            entity.HasKey(x => x.SecondaryIndexName);
            entity.Property(x => x.SecondaryIndexName).HasMaxLength(200);
            entity.Property(x => x.TotalCount);
            entity.Property(x => x.UpdatedAt);
        });

        modelBuilder.Entity<DailyCommitPositionEntity>(entity =>
        {
            entity.ToTable("DailyCommitPositions");
            entity.HasKey(x => new { x.SubscriptionId, x.SecondaryIndexName, x.Date });
            entity.Property(x => x.SubscriptionId).HasMaxLength(200);
            entity.Property(x => x.SecondaryIndexName).HasMaxLength(200);
            entity.Property(x => x.Date);
            entity.Property(x => x.CommitPosition);
        });

        modelBuilder.Entity<IndexScanStateEntity>(entity =>
        {
            entity.ToTable("IndexScanStates");
            entity.HasKey(x => x.SecondaryIndexName);
            entity.Property(x => x.SecondaryIndexName).HasMaxLength(200);
            entity.Property(x => x.LastScannedCommitPosition);
        });
    }
}
