using CatchupService.Domain;

namespace CatchupService.Infrastructure.Persistence;

internal static class StoreMappings
{
    public static SubscriptionDefinition ToDomain(this SubscriptionConfigurationEntity entity) =>
        new(
            entity.SubscriptionId,
            entity.SecondaryIndexName,
            entity.EndpointUrl,
            entity.Tag,
            new TimeoutSettings(TimeSpan.FromTicks(entity.RequestTimeoutTicks), TimeSpan.FromTicks(entity.PollIntervalTicks)),
            new RetrySettings(entity.RetryMaxAttempts, TimeSpan.FromTicks(entity.RetryDelayTicks)),
            new CheckpointSettings(entity.CheckpointBatchSize),
            string.IsNullOrWhiteSpace(entity.AuthenticationScheme) && string.IsNullOrWhiteSpace(entity.AuthenticationParametersJson)
                ? null
                : new AuthenticationConfiguration(
                    entity.AuthenticationScheme,
                    JsonPersistence.DeserializeDictionary(entity.AuthenticationParametersJson)));

    public static SubscriptionConfigurationEntity ToEntity(this SubscriptionDefinition subscription) =>
        new()
        {
            SubscriptionId = subscription.SubscriptionId,
            SecondaryIndexName = subscription.SecondaryIndexName,
            EndpointUrl = subscription.EndpointUrl,
            Tag = subscription.Tag,
            RequestTimeoutTicks = subscription.Timeout.RequestTimeout.Ticks,
            PollIntervalTicks = subscription.Timeout.PollInterval.Ticks,
            RetryMaxAttempts = subscription.Retry.MaxAttempts,
            RetryDelayTicks = subscription.Retry.Delay.Ticks,
            CheckpointBatchSize = subscription.Checkpoint.BatchSize,
            AuthenticationScheme = subscription.Authentication?.Scheme,
            AuthenticationParametersJson = subscription.Authentication is null
                ? null
                : JsonPersistence.Serialize(subscription.Authentication.Parameters)
        };

    public static ParkedEventEntity ToEntity(this ParkedEvent parkedEvent) =>
        new()
        {
            ParkedEventId = parkedEvent.ParkedEventId,
            SubscriptionId = parkedEvent.SubscriptionId,
            EventId = parkedEvent.EventId,
            SequenceNumber = parkedEvent.SequenceNumber,
            StreamName = parkedEvent.StreamName,
            EventType = parkedEvent.EventType,
            OccurredAt = parkedEvent.OccurredAt,
            FailureReason = parkedEvent.FailureReason,
            Payload = parkedEvent.Payload,
            ContentType = parkedEvent.ContentType,
            MetadataJson = JsonPersistence.Serialize(parkedEvent.Metadata),
            ParkedAt = parkedEvent.ParkedAt,
            AttemptCount = parkedEvent.AttemptCount
        };

    public static ParkedEvent ToDomain(this ParkedEventEntity entity) =>
        new(
            entity.ParkedEventId,
            entity.SubscriptionId,
            entity.EventId,
            entity.SequenceNumber,
            entity.StreamName,
            entity.EventType,
            entity.OccurredAt,
            entity.FailureReason,
            entity.Payload,
            entity.ContentType,
            JsonPersistence.DeserializeDictionary(entity.MetadataJson),
            entity.ParkedAt,
            entity.AttemptCount);

    public static ReplaySession ToDomain(this ReplaySessionEntity entity) =>
        new(
            entity.ReplaySessionId,
            entity.SubscriptionId,
            entity.StartedAt,
            entity.CompletedAt);
}
