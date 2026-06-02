namespace CatchupService.Infrastructure.Persistence;

public sealed class SubscriptionConfigurationEntity
{
    public string SubscriptionId { get; set; } = string.Empty;

    public string SecondaryIndexName { get; set; } = string.Empty;

    public string EndpointUrl { get; set; } = string.Empty;

    public string Tag { get; set; } = string.Empty;

    public long RequestTimeoutSeconds { get; set; }

    public int RetryMaxAttempts { get; set; }

    public long RetryDelaySeconds { get; set; }

    public int CheckpointBatchSize { get; set; }

    public string? AuthenticationScheme { get; set; }

    public string? AuthenticationParametersJson { get; set; }
}

public sealed class SubscriptionCheckpointEntity
{
    public string SubscriptionId { get; set; } = string.Empty;

    public long Checkpoint { get; set; }
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
}

public sealed class ReplaySessionEntity
{
    public Guid ReplaySessionId { get; set; }

    public string SubscriptionId { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}
