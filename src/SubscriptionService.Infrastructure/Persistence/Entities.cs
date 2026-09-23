using SubscriptionService.Domain;

namespace SubscriptionService.Infrastructure.Persistence;

public sealed class EndpointEntity
{
    public int EndpointId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? AuthenticationScheme { get; set; }
    public string? AuthenticationParametersJson { get; set; }
    public ICollection<SubscriptionConfigurationEntity> Subscriptions { get; set; } = [];
}

public sealed class SubscriptionConfigurationEntity
{
    public string SubscriptionId { get; set; } = string.Empty;

    public string SecondaryIndexName { get; set; } = string.Empty;

    public int EndpointId { get; set; }

    public string Tag { get; set; } = string.Empty;

    public long RequestTimeoutSeconds { get; set; }

    public int RetryMaxAttempts { get; set; }

    public long RetryDelaySeconds { get; set; }

    public int CheckpointBatchSize { get; set; }

    public bool ContinueOnParked { get; set; }

    public bool Enabled { get; set; } = true;

    public string OperationalState { get; set; } = SubscriptionOperationalState.Healthy.ToString();

    public string? OperationalReason { get; set; }

    public bool SoftDeleteParked { get; set; } = true;

    public bool EnableEventLogging { get; set; }

    public EndpointEntity Endpoint { get; set; } = null!;
}

public sealed class SubscriptionEventLogEntity
{
    public long SubscriptionEventLogId { get; set; }
    public string EventId { get; set; } = string.Empty;
    public string SubscriptionId { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public string AddressSentTo { get; set; } = string.Empty;
    public int? ResponseStatusCode { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? ResponseBody { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
    public int DeliveryAttempt { get; set; }
    public bool IsReplay { get; set; }
}

public sealed class SubscriptionCheckpointEntity
{
    public string SubscriptionId { get; set; } = string.Empty;

    // Sequence checkpoint (numeric sequence) removed - we persist commit position and processed count instead.
    public long? CommitPosition { get; set; }

    // EventStore subscriptions also need the prepare position to resume precisely after restart.
    public long? PreparePosition { get; set; }

    public long ProcessedCount { get; set; }

    // Optional reason describing why a checkpoint was taken (e.g. "batch-save", "manual-replay", etc.)
    public string? CheckpointReason { get; set; }
}

public sealed class ParkedEventEntity
{
    public Guid ParkedEventId { get; set; }

    public string SubscriptionId { get; set; } = string.Empty;

    public string EventId { get; set; } = string.Empty;

    public long SequenceNumber { get; set; }

    public string StreamName { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    public string FailureReason { get; set; } = string.Empty;

    public byte[] Payload { get; set; } = [];

    public string ContentType { get; set; } = string.Empty;

    public string MetadataJson { get; set; } = string.Empty;

    public DateTimeOffset ParkedAt { get; set; }

    public int AttemptCount { get; set; }

    public bool IsDeleted { get; set; }
}

public sealed class ReplaySessionEntity
{
    public Guid ReplaySessionId { get; set; }

    public string SubscriptionId { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class StreamEventCountEntity
{
    public string SubscriptionId { get; set; } = string.Empty;

    public string SecondaryIndexName { get; set; } = string.Empty;

    public long TotalCount { get; set; }

    public long? LastScannedCommitPosition { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class DailyCommitPositionEntity
{
    public string SubscriptionId { get; set; } = string.Empty;

    public string SecondaryIndexName { get; set; } = string.Empty;

    public DateTime Date { get; set; }

    public long? CommitPosition { get; set; }
}

public sealed class IndexScanStateEntity
{
    public string SecondaryIndexName { get; set; } = string.Empty;

    public long? LastScannedCommitPosition { get; set; }
}

public sealed class BuiltInIndexCatalogEntity
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public long? LastSeenCommitPosition { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
