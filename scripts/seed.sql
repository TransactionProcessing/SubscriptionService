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
        RequestTimeoutTicks,
        PollIntervalTicks,
        RetryMaxAttempts,
        RetryDelayTicks,
        CheckpointBatchSize,
        AuthenticationScheme,
        AuthenticationParametersJson
    )
    VALUES (
        N'sample-subscription',
        N'sample-index',
        N'https://example.test/subscriptions/sample-subscription',
        N'orders',
        300000000,
        50000000,
        3,
        10000000,
        100,
        NULL,
        NULL
    );
END;
