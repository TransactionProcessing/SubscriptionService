using SubscriptionService.Worker.Ui;

namespace SubscriptionService.Tests;

public sealed class SubscriptionEditorModelTests
{
    private const string BuiltInEventTypeIndexName = "$idx-et-OrderPlaced";

    [Fact]
    public void BuildGeneratedSubscriptionId_joins_index_and_endpoint_without_spaces()
    {
        var id = SubscriptionEditorModel.BuildGeneratedSubscriptionId("$idx-ce-File Aggregate", "File Processor", "Orders Tag");

        Assert.Equal("$idx-ce-FileAggregate_FileProcessor_OrdersTag", id);
    }

    [Fact]
    public void CreateDefault_UsesZeroRetryMaxAttempts()
    {
        var model = SubscriptionEditorModel.CreateDefault();

        Assert.Equal(0, model.RetryMaxAttempts);
    }

    [Fact]
    public void BuildDerivedSecondaryIndexName_DoesNotAppendSeparatorWithoutSuffix()
    {
        var name = SubscriptionEditorModel.BuildDerivedSecondaryIndexName("orders-by-user", string.Empty);

        Assert.Equal("$idx-user-orders-by-user", name);
    }

    [Fact]
    public void NormalizeSelectedIndexName_PrefixesExistingUserIndexForSubscriptionConfiguration()
    {
        var name = SubscriptionEditorModel.NormalizeSelectedIndexName("merchant-balance-archive-events", isBuiltIn: false);

        Assert.Equal("$idx-user-merchant-balance-archive-events", name);
    }

    [Fact]
    public void NormalizeSelectedIndexName_PreservesBuiltInAndAlreadyPrefixedIndexes()
    {
        Assert.Equal(
            BuiltInEventTypeIndexName,
            SubscriptionEditorModel.NormalizeSelectedIndexName(BuiltInEventTypeIndexName, isBuiltIn: true));
        Assert.Equal(
            "$idx-user-orders-by-user",
            SubscriptionEditorModel.NormalizeSelectedIndexName("$idx-user-orders-by-user", isBuiltIn: false));
    }

    [Fact]
    public void NormalizeManuallyEnteredIndexName_PrefixesUserNamesAndPreservesKnownBuiltIns()
    {
        var builtInIndexes = new[] { BuiltInEventTypeIndexName };

        Assert.Equal(
            "$idx-user-orders-by-country",
            SubscriptionEditorModel.NormalizeManuallyEnteredIndexName("orders-by-country", builtInIndexes));
        Assert.Equal(
            BuiltInEventTypeIndexName,
            SubscriptionEditorModel.NormalizeManuallyEnteredIndexName(BuiltInEventTypeIndexName, builtInIndexes));
    }

    [Fact]
    public void TryParseDerivedSecondaryIndexName_RecognizesIndexWithoutSuffix()
    {
        var parsed = SubscriptionEditorModel.TryParseDerivedSecondaryIndexName(
            "$idx-user-orders-by-user",
            out var existingIndexName,
            out var suffix);

        Assert.True(parsed);
        Assert.Equal("orders-by-user", existingIndexName);
        Assert.Equal(string.Empty, suffix);
    }

    [Fact]
    public void TryCreateSubscription_PreservesZeroRetryMaxAttempts()
    {
        var model = new SubscriptionEditorModel
        {
            SubscriptionId = "sub-1",
            SecondaryIndexName = "index-1",
            EndpointUrl = "https://example.test/subscriptions/sub-1",
            Tag = "orders",
            RequestTimeoutSeconds = 30,
            RetryMaxAttempts = 0,
            RetryDelaySeconds = 1,
            CheckpointBatchSize = 100,
            Enabled = true,
            SoftDeleteParked = true,
            AuthenticationParametersJson = "{}"
        };

        var created = model.TryCreateSubscription(out var subscription, out var error);

        Assert.True(created);
        Assert.Null(error);
        Assert.NotNull(subscription);
        Assert.Equal(0, subscription!.Retry.MaxAttempts);
    }

    [Fact]
    public void TryCreateSubscription_PreservesEventLoggingFlag()
    {
        var model = new SubscriptionEditorModel
        {
            SubscriptionId = "sub-1",
            SecondaryIndexName = "index-1",
            EndpointUrl = "https://example.test/subscriptions/sub-1",
            Tag = "orders",
            EndpointId = 1,
            EnableEventLogging = true
        };

        Assert.True(model.TryCreateSubscription(out var subscription, out _));
        Assert.True(subscription!.EnableEventLogging);
    }
}

