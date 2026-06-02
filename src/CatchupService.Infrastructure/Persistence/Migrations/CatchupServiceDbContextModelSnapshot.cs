using CatchupService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace CatchupService.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CatchupServiceDbContext))]
public partial class CatchupServiceDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.8");

        modelBuilder.Entity("CatchupService.Infrastructure.Persistence.ParkedEventEntity", b =>
        {
            b.Property<Guid>("ParkedEventId");
            b.Property<int>("AttemptCount");
            b.Property<string>("ContentType").HasMaxLength(200);
            b.Property<byte[]>("Payload");
            b.Property<DateTimeOffset>("ParkedAt");
            b.Property<string>("EventId").HasMaxLength(200);
            b.Property<string>("EventType").HasMaxLength(200);
            b.Property<string>("FailureReason").HasMaxLength(4000);
            b.Property<string>("MetadataJson");
            b.Property<DateTimeOffset>("OccurredAt");
            b.Property<long>("SequenceNumber");
            b.Property<string>("StreamName").HasMaxLength(512);
            b.Property<string>("SubscriptionId").HasMaxLength(200);
            b.HasKey("ParkedEventId");
            b.HasIndex("SubscriptionId", "SequenceNumber");
            b.ToTable("ParkedEvents");
        });

        modelBuilder.Entity("CatchupService.Infrastructure.Persistence.ReplaySessionEntity", b =>
        {
            b.Property<Guid>("ReplaySessionId");
            b.Property<DateTimeOffset?>("CompletedAt");
            b.Property<DateTimeOffset>("StartedAt");
            b.Property<string>("SubscriptionId").HasMaxLength(200);
            b.HasKey("ReplaySessionId");
            b.HasIndex("SubscriptionId");
            b.ToTable("ReplaySessions");
        });

        modelBuilder.Entity("CatchupService.Infrastructure.Persistence.SubscriptionCheckpointEntity", b =>
        {
            b.Property<string>("SubscriptionId").HasMaxLength(200);
            b.Property<long>("Checkpoint");
            b.HasKey("SubscriptionId");
            b.ToTable("SubscriptionCheckpoints");
        });

        modelBuilder.Entity("CatchupService.Infrastructure.Persistence.SubscriptionConfigurationEntity", b =>
        {
            b.Property<string>("SubscriptionId").HasMaxLength(200);
            b.Property<int>("CheckpointBatchSize");
            b.Property<string>("EndpointUrl").HasMaxLength(2048);
            b.Property<string>("AuthenticationScheme").HasMaxLength(200);
            b.Property<string>("AuthenticationParametersJson");
            b.Property<string>("SecondaryIndexName").HasMaxLength(200);
            b.Property<string>("Tag").HasMaxLength(200);
            b.Property<long>("RequestTimeoutSeconds");
            b.Property<int>("RetryMaxAttempts");
            b.Property<long>("RetryDelaySeconds");
            b.HasKey("SubscriptionId");
            b.ToTable("SubscriptionConfigurations");
        });
    }
}
