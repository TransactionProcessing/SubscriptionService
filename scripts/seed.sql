IF NOT EXISTS (
    SELECT 1
    FROM dbo.SubscriptionConfigurations
    WHERE SubscriptionId = N'sample-subscription'
)
BEGIN
    INSERT INTO dbo.SubscriptionConfigurations (
        SubscriptionId,
        SecondaryIndexName,
        EndpointUrl,
        Tag,
        RequestTimeoutSeconds,
        RetryMaxAttempts,
        RetryDelaySeconds,
        CheckpointBatchSize,
        AuthenticationScheme,
        AuthenticationParametersJson
    )
    VALUES (
        N'sample-subscription',
        N'sample-index',
        N'https://example.test/subscriptions/sample-subscription',
        N'orders',
        30,
        3,
        1,
        100,
        NULL,
        NULL
    );
END;
