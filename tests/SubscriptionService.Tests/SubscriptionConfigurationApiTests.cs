using Microsoft.AspNetCore.Http;
using SubscriptionService.Application;
using SubscriptionService.Domain;
using SubscriptionService.Worker;

namespace SubscriptionService.Tests;

public sealed class SubscriptionConfigurationApiTests
{
    [Fact]
    public void Valid_request_maps_to_subscription_definition()
    {
        var request = new SubscriptionConfigurationRequest
        {
            SubscriptionId = "orders-catchup",
            SecondaryIndexName = "orders-by-tenant",
            EndpointUrl = "https://consumer.example.test/events",
            Tag = "orders",
            TimeoutSeconds = 20,
            RetryMaxAttempts = 5,
            RetryDelaySeconds = 2,
            CheckpointBatchSize = 50,
            ContinueOnParked = true,
            SoftDeleteParked = false
        };

        var result = SubscriptionConfigurationRequestMapper.Map(request);

        Assert.Empty(result.Errors);
        Assert.NotNull(result.Subscription);
        Assert.Equal("orders-catchup", result.Subscription!.SubscriptionId);
        Assert.Equal(TimeSpan.FromSeconds(20), result.Subscription.Timeout.RequestTimeout);
        Assert.Equal(5, result.Subscription.Retry.MaxAttempts);
        Assert.Equal(TimeSpan.FromSeconds(2), result.Subscription.Retry.Delay);
        Assert.Equal(50, result.Subscription.Checkpoint.BatchSize);
        Assert.True(result.Subscription.ContinueOnParked);
        Assert.False(result.Subscription.SoftDeleteParked);
    }

    [Fact]
    public void Invalid_request_returns_field_errors()
    {
        var request = new SubscriptionConfigurationRequest
        {
            SubscriptionId = " ",
            SecondaryIndexName = "",
            EndpointUrl = "ftp://consumer.example.test/events",
            Tag = "",
            TimeoutSeconds = 0,
            RetryMaxAttempts = 0,
            RetryDelaySeconds = -1,
            CheckpointBatchSize = 0
        };

        var result = SubscriptionConfigurationRequestMapper.Map(request);

        Assert.Null(result.Subscription);
        Assert.Contains("SubscriptionId", result.Errors.Keys);
        Assert.Contains("SecondaryIndexName", result.Errors.Keys);
        Assert.Contains("EndpointUrl", result.Errors.Keys);
        Assert.Contains("Tag", result.Errors.Keys);
        Assert.Contains("TimeoutSeconds", result.Errors.Keys);
        Assert.Contains("RetryDelaySeconds", result.Errors.Keys);
        Assert.Contains("CheckpointBatchSize", result.Errors.Keys);
    }

    [Fact]
    public async Task Create_returns_conflict_when_subscription_id_already_exists()
    {
        var store = new TestSubscriptionConfigurationStore(CreateSubscription("orders-catchup"));
        var request = new SubscriptionConfigurationRequest
        {
            SubscriptionId = "orders-catchup",
            SecondaryIndexName = "orders-by-tenant",
            EndpointUrl = "https://consumer.example.test/events",
            Tag = "orders"
        };

        var result = await SubscriptionConfigurationEndpoints.CreateAsync(request, store);
        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);

        Assert.Equal(StatusCodes.Status409Conflict, statusResult.StatusCode);
        Assert.Equal(0, store.UpsertCount);
    }

    private static SubscriptionDefinition CreateSubscription(string subscriptionId) => new(
        subscriptionId,
        "orders-by-tenant",
        "https://consumer.example.test/events",
        "orders",
        TimeoutSettings.Default,
        RetrySettings.Default,
        CheckpointSettings.Default);

    private sealed class TestSubscriptionConfigurationStore(SubscriptionDefinition? existing = null) : ISubscriptionConfigurationStore
    {
        private readonly List<SubscriptionDefinition> _subscriptions = existing is null ? [] : [existing];

        public int UpsertCount { get; private set; }

        public Task<IReadOnlyCollection<SubscriptionDefinition>> GetSubscriptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<SubscriptionDefinition>>(this._subscriptions);

        public Task UpsertAsync(SubscriptionDefinition subscription, CancellationToken cancellationToken = default)
        {
            this.UpsertCount++;
            this._subscriptions.Add(subscription);
            return Task.CompletedTask;
        }

        public Task SetOperationalStateAsync(string subscriptionId, SubscriptionOperationalState operationalState, string? operationalReason = null, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RemoveAsync(string subscriptionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
