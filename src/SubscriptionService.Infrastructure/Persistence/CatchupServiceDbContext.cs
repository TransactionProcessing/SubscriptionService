using Microsoft.EntityFrameworkCore;

namespace SubscriptionService.Infrastructure.Persistence;

public sealed class CatchupServiceDbContext(DbContextOptions<CatchupServiceDbContext> options) : DbContext(options)
{
    public DbSet<SubscriptionConfigurationEntity> SubscriptionConfigurations => this.Set<SubscriptionConfigurationEntity>();
    public DbSet<SubscriptionEventLogEntity> SubscriptionEventLogs => this.Set<SubscriptionEventLogEntity>();
    public DbSet<EndpointEntity> Endpoints => this.Set<EndpointEntity>();

    public DbSet<SubscriptionCheckpointEntity> SubscriptionCheckpoints => this.Set<SubscriptionCheckpointEntity>();

    public DbSet<ParkedEventEntity> ParkedEvents => this.Set<ParkedEventEntity>();

    public DbSet<ReplaySessionEntity> ReplaySessions => this.Set<ReplaySessionEntity>();

    public DbSet<StreamEventCountEntity> StreamEventCounts => this.Set<StreamEventCountEntity>();
    public DbSet<DailyCommitPositionEntity> DailyCommitPositions => this.Set<DailyCommitPositionEntity>();
    public DbSet<IndexScanStateEntity> IndexScanStates => this.Set<IndexScanStateEntity>();
    public DbSet<BuiltInIndexCatalogEntity> BuiltInIndexCatalog => this.Set<BuiltInIndexCatalogEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EndpointEntity>(entity =>
        {
            entity.ToTable("Endpoints");
            entity.HasKey(x => x.EndpointId);
            entity.Property(x => x.EndpointId).ValueGeneratedOnAdd();
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.Url).HasMaxLength(2048);
            entity.Property(x => x.AuthenticationScheme).HasMaxLength(200);
        });

        modelBuilder.Entity<SubscriptionConfigurationEntity>(entity =>
        {
            entity.ToTable("SubscriptionConfigurations");
            entity.HasKey(x => x.SubscriptionId);
            entity.Property(x => x.SubscriptionId).HasMaxLength(200);
            entity.Property(x => x.SecondaryIndexName).HasMaxLength(200);
            entity.Property(x => x.EndpointId);
            entity.HasOne(x => x.Endpoint).WithMany(x => x.Subscriptions).HasForeignKey(x => x.EndpointId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.Tag).HasMaxLength(200);
            entity.Property(x => x.ContinueOnParked);
            entity.Property(x => x.Enabled);
            entity.Property(x => x.OperationalState).HasMaxLength(32);
            entity.Property(x => x.OperationalReason).HasMaxLength(4000);
            entity.Property(x => x.SoftDeleteParked).HasDefaultValue(true);
            entity.Property(x => x.EnableEventLogging).HasDefaultValue(false);
        });

        modelBuilder.Entity<SubscriptionEventLogEntity>(entity =>
        {
            entity.ToTable("SubscriptionEventLogs");
            entity.HasKey(x => x.SubscriptionEventLogId);
            entity.Property(x => x.SubscriptionEventLogId).ValueGeneratedOnAdd();
            entity.Property(x => x.EventId).HasMaxLength(200);
            entity.Property(x => x.SubscriptionId).HasMaxLength(200);
            entity.Property(x => x.Payload);
            entity.Property(x => x.AddressSentTo).HasMaxLength(2048);
            entity.Property(x => x.EventType).HasMaxLength(200);
            entity.Property(x => x.ResponseBody);
            entity.Property(x => x.ProcessedAt);
            entity.HasIndex(x => new { x.SubscriptionId, x.ProcessedAt });
        });

        modelBuilder.Entity<SubscriptionCheckpointEntity>(entity =>
        {
            entity.ToTable("SubscriptionCheckpoints");
            entity.HasKey(x => x.SubscriptionId);
            entity.Property(x => x.SubscriptionId).HasMaxLength(200);
            entity.Property(x => x.CommitPosition);
            entity.Property(x => x.PreparePosition);
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
            entity.HasKey(x => x.SubscriptionId);
            entity.Property(x => x.SubscriptionId).HasMaxLength(200);
            entity.Property(x => x.SecondaryIndexName).HasMaxLength(200);
            entity.Property(x => x.TotalCount);
            entity.Property(x => x.LastScannedCommitPosition);
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

        modelBuilder.Entity<BuiltInIndexCatalogEntity>(entity =>
        {
            entity.ToTable("BuiltInIndexCatalog");
            entity.HasKey(x => x.Name);
            entity.Property(x => x.Name).HasMaxLength(512);
            entity.Property(x => x.Type).HasMaxLength(64);
            entity.Property(x => x.LastSeenCommitPosition);
            entity.Property(x => x.UpdatedAt);
        });
    }
}
