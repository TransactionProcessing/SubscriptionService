using SubscriptionService.Domain;

namespace SubscriptionService.Infrastructure.Persistence;

internal static class StoreMappings
{
    public static EndpointDefinition ToDomain(this EndpointEntity entity) =>
        new(entity.EndpointId, entity.Name, entity.Url,
            string.IsNullOrWhiteSpace(entity.AuthenticationScheme) && string.IsNullOrWhiteSpace(entity.AuthenticationParametersJson)
                ? null
                : new AuthenticationConfiguration(entity.AuthenticationScheme, JsonPersistence.DeserializeDictionary(entity.AuthenticationParametersJson)));

    public static EndpointEntity ToEntity(this EndpointDefinition endpoint) => new()
    {
        EndpointId = endpoint.EndpointId,
        Name = endpoint.Name,
        Url = endpoint.Url,
        AuthenticationScheme = endpoint.Authentication?.Scheme,
        AuthenticationParametersJson = endpoint.Authentication is null ? null : JsonPersistence.Serialize(endpoint.Authentication.Parameters)
    };

    public static SubscriptionDefinition ToDomain(this SubscriptionConfigurationEntity entity)
    {
        Enum.TryParse<SubscriptionOperationalState>(entity.OperationalState, true, out var operationalState);
        var endpoint = entity.Endpoint.ToDomain();
        var def = new SubscriptionDefinition(
            entity.SubscriptionId,
            entity.SecondaryIndexName,
            endpoint.Url,
            entity.Tag,
            new TimeoutSettings(TimeSpan.FromSeconds(entity.RequestTimeoutSeconds)),
            new RetrySettings(entity.RetryMaxAttempts, TimeSpan.FromSeconds(entity.RetryDelaySeconds)),
            new CheckpointSettings(entity.CheckpointBatchSize),
            entity.ContinueOnParked,
            endpoint.Authentication)
        { EndpointId = endpoint.EndpointId };

        return def with
        {
            Enabled = entity.Enabled,
            OperationalState = operationalState,
            OperationalReason = entity.OperationalReason,
            SoftDeleteParked = entity.SoftDeleteParked,
            EnableEventLogging = entity.EnableEventLogging
        };
    }

    public static SubscriptionConfigurationEntity ToEntity(this SubscriptionDefinition subscription) =>
        new()
        {
            SubscriptionId = subscription.SubscriptionId,
            SecondaryIndexName = subscription.SecondaryIndexName,
            EndpointId = subscription.EndpointId ?? throw new InvalidOperationException("Subscription must reference an endpoint."),
            Tag = subscription.Tag,
            RequestTimeoutSeconds = Math.Max(0L, (long)Math.Ceiling(subscription.Timeout.RequestTimeout.TotalSeconds)),
            RetryMaxAttempts = subscription.Retry.MaxAttempts,
            RetryDelaySeconds = Math.Max(0L, (long)Math.Ceiling(subscription.Retry.Delay.TotalSeconds)),
            CheckpointBatchSize = subscription.Checkpoint.BatchSize,
            ContinueOnParked = subscription.ContinueOnParked,
            Enabled = subscription.Enabled,
            OperationalState = subscription.OperationalState.ToString(),
            OperationalReason = subscription.OperationalReason,
            SoftDeleteParked = subscription.SoftDeleteParked,
            EnableEventLogging = subscription.EnableEventLogging,
        };

    public static ParkedEventEntity ToEntity(this ParkedEvent parkedEvent) =>
        new()
        {
            ParkedEventId = parkedEvent.ParkedEventId,
            SubscriptionId = parkedEvent.SubscriptionId,
            EventId = parkedEvent.EventId,
            // SequenceNumber removed from domain; legacy DB column remains for ordering but not provided by domain events
            SequenceNumber = 0,
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
