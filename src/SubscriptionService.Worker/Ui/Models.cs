using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using SubscriptionService.Application;
using SubscriptionService.Domain;

namespace SubscriptionService.Worker.Ui;

public sealed record DashboardViewModel(
    int TotalSubscriptions,
    int EnabledSubscriptions,
    int RunningSubscriptions,
    int ReplayingSubscriptions,
    int ParkedEvents,
    IReadOnlyList<DashboardSubscriptionCard> Subscriptions);

public sealed record DashboardSubscriptionCard(
    string SubscriptionId,
    string SecondaryIndexName,
    string EndpointUrl,
    string Tag,
    bool EnableEventLogging,
    bool Enabled,
    bool IsRunning,
    string Health,
    SubscriptionOperationalState OperationalState,
    string? OperationalReason,
    bool HasActiveReplaySession,
    int ParkedEventCount,
    long ProcessedCount,
    long? CommitPosition,
    string? ProgressDisplay,
    DateTimeOffset? LatestParkedAt,
    string? LatestParkedFailureReason);

public sealed record EndpointViewModel(int EndpointId, string Name, string Url, string? AuthenticationScheme);

public sealed class EndpointEditorModel
{
    public int EndpointId { get; set; }
    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;
    [Required, Url, StringLength(2048)]
    public string Url { get; set; } = string.Empty;
    [StringLength(120)]
    public string? AuthenticationScheme { get; set; }
    public string AuthenticationParametersJson { get; set; } = "{}";

    public bool TryCreate(out EndpointDefinition? endpoint, out string? error)
    {
        endpoint = null;
        error = null;
        try
        {
            var parameters = string.IsNullOrWhiteSpace(this.AuthenticationParametersJson) || this.AuthenticationParametersJson.Trim() == "{}"
                ? new Dictionary<string, string>()
                : JsonSerializer.Deserialize<Dictionary<string, string>>(this.AuthenticationParametersJson) ?? [];
            endpoint = new EndpointDefinition(this.EndpointId, this.Name.Trim(), this.Url.Trim(),
                string.IsNullOrWhiteSpace(this.AuthenticationScheme) && parameters.Count == 0
                    ? null
                    : new AuthenticationConfiguration(this.AuthenticationScheme?.Trim(), parameters));
            return true;
        }
        catch (JsonException)
        {
            error = "Authentication parameters must be valid JSON for an object of string values.";
            return false;
        }
    }
}

public sealed record SubscriptionDetailViewModel(
    SubscriptionDefinition Subscription,
    SubscriptionStatus? Status,
    ReplayStatus? ReplayStatus,
    CheckpointState Checkpoint,
    IReadOnlyCollection<DailyCommitPositionRecord> DailyPositions,
    IReadOnlyCollection<ParkedEvent> ParkedEvents,
    DateTime FromDate,
    DateTime ToDate);

public sealed class SubscriptionEditorModel
{
    private const string DerivedSecondaryIndexPrefix = "$idx-user-";

    private static readonly JsonSerializerOptions PrettyJsonOptions = new()
    {
        WriteIndented = true
    };

    [Required, StringLength(120)]
    public string SubscriptionId { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "Please select an endpoint.")]
    public int EndpointId { get; set; }

    [StringLength(200)]
    public string SecondaryIndexName { get; set; } = string.Empty;

    [StringLength(2048)]
    public string EndpointUrl { get; set; } = string.Empty;

    [Required, StringLength(120)]
    public string Tag { get; set; } = string.Empty;

    [Range(1, 3600)]
    public int RequestTimeoutSeconds { get; set; } = 30;

    [Range(0, 100)]
    public int RetryMaxAttempts { get; set; } = 0;

    [Range(0, 3600)]
    public int RetryDelaySeconds { get; set; } = 1;

    [Range(1, 5000)]
    public int CheckpointBatchSize { get; set; } = 100;

    public bool ContinueOnParked { get; set; }

    public bool Enabled { get; set; } = true;

    public SubscriptionOperationalState OperationalState { get; set; } = SubscriptionOperationalState.Healthy;

    public string? OperationalReason { get; set; }

    public bool SoftDeleteParked { get; set; } = true;

    public bool EnableEventLogging { get; set; }

    [StringLength(120)]
    public string? AuthenticationScheme { get; set; }

    public string AuthenticationParametersJson { get; set; } = "{}";

    public static SubscriptionEditorModel CreateDefault() => new();

    public static string GetDerivedSecondaryIndexPrefix(string existingIndexName)
    {
        var trimmedIndexName = existingIndexName.Trim();
        return string.IsNullOrWhiteSpace(trimmedIndexName)
            ? string.Empty
            : $"{DerivedSecondaryIndexPrefix}{trimmedIndexName}:";
    }

    public static string BuildDerivedSecondaryIndexName(string existingIndexName, string suffix)
    {
        var prefix = GetDerivedSecondaryIndexPrefix(existingIndexName);
        var trimmedSuffix = suffix.Trim();
        return string.IsNullOrWhiteSpace(prefix)
            ? trimmedSuffix
            : string.IsNullOrWhiteSpace(trimmedSuffix)
                ? prefix.TrimEnd(':')
                : $"{prefix}{trimmedSuffix}";
    }

    public static bool TryParseDerivedSecondaryIndexName(string? secondaryIndexName, out string? existingIndexName, out string? suffix)
    {
        existingIndexName = null;
        suffix = null;

        var trimmedIndexName = secondaryIndexName?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedIndexName) || !trimmedIndexName.StartsWith(DerivedSecondaryIndexPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var remainder = trimmedIndexName[DerivedSecondaryIndexPrefix.Length..];
        var separatorIndex = remainder.IndexOf(':');
        if (separatorIndex < 0)
        {
            existingIndexName = remainder.Trim();
            suffix = string.Empty;
            return !string.IsNullOrWhiteSpace(existingIndexName);
        }

        existingIndexName = remainder[..separatorIndex].Trim();
        suffix = remainder[(separatorIndex + 1)..];
        return !string.IsNullOrWhiteSpace(existingIndexName);
    }

    public static string BuildGeneratedSubscriptionId(string indexName, string endpointName, string tag)
    {
        static string RemoveWhitespace(string value) => string.Concat(value.Where(character => !char.IsWhiteSpace(character)));

        var index = RemoveWhitespace(indexName.Trim());
        var endpoint = RemoveWhitespace(endpointName.Trim());
        var normalizedTag = RemoveWhitespace(tag.Trim());
        var parts = new[] { index, endpoint, normalizedTag }.Where(part => !string.IsNullOrWhiteSpace(part));
        return string.Join('_', parts);
    }

    public static SubscriptionEditorModel FromDomain(SubscriptionDefinition subscription)
    {
        return new SubscriptionEditorModel
        {
            SubscriptionId = subscription.SubscriptionId,
            EndpointId = subscription.EndpointId ?? 0,
            SecondaryIndexName = subscription.SecondaryIndexName,
            EndpointUrl = subscription.EndpointUrl,
            Tag = subscription.Tag,
            RequestTimeoutSeconds = (int)Math.Max(1, subscription.Timeout.RequestTimeout.TotalSeconds),
            RetryMaxAttempts = subscription.Retry.MaxAttempts,
            RetryDelaySeconds = (int)Math.Max(0, subscription.Retry.Delay.TotalSeconds),
            CheckpointBatchSize = subscription.Checkpoint.BatchSize,
            ContinueOnParked = subscription.ContinueOnParked,
            Enabled = subscription.Enabled,
            OperationalState = subscription.OperationalState,
            OperationalReason = subscription.OperationalReason,
            SoftDeleteParked = subscription.SoftDeleteParked,
            EnableEventLogging = subscription.EnableEventLogging,
            AuthenticationScheme = subscription.Authentication?.Scheme,
            AuthenticationParametersJson = subscription.Authentication is null
                ? "{}"
                : JsonSerializer.Serialize(subscription.Authentication.Parameters, PrettyJsonOptions)
        };
    }

    public bool TryCreateSubscription(out SubscriptionDefinition? subscription, out string? error)
    {
        subscription = null;
        error = null;

        if (!this.TryBuildAuthentication(out var authentication, out error))
        {
            return false;
        }

        subscription = new SubscriptionDefinition(
            this.SubscriptionId.Trim(),
            this.SecondaryIndexName.Trim(),
            this.EndpointUrl.Trim(),
            this.Tag.Trim(),
            new TimeoutSettings(TimeSpan.FromSeconds(Math.Max(1, this.RequestTimeoutSeconds))),
            new RetrySettings(Math.Max(0, this.RetryMaxAttempts), TimeSpan.FromSeconds(Math.Max(0, this.RetryDelaySeconds))),
            new CheckpointSettings(Math.Max(1, this.CheckpointBatchSize)),
            this.ContinueOnParked,
            authentication)
        {
            EndpointId = this.EndpointId,
            Enabled = this.Enabled,
            OperationalState = this.OperationalState,
            OperationalReason = this.OperationalReason?.Trim(),
            SoftDeleteParked = this.SoftDeleteParked,
            EnableEventLogging = this.EnableEventLogging
        };

        return true;
    }

    private bool TryBuildAuthentication(out AuthenticationConfiguration? authentication, out string? error)
    {
        authentication = null;
        error = null;

        var scheme = string.IsNullOrWhiteSpace(this.AuthenticationScheme)
            ? null
            : this.AuthenticationScheme.Trim();

        var json = this.AuthenticationParametersJson?.Trim();
        if (string.IsNullOrWhiteSpace(json) || string.Equals(json, "{}", StringComparison.Ordinal))
        {
            var parameters = new Dictionary<string, string>();
            authentication = scheme is null ? null : new AuthenticationConfiguration(scheme, parameters);
            return true;
        }

        try
        {
            var parameters = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
            authentication = scheme is null && parameters.Count == 0
                ? null
                : new AuthenticationConfiguration(scheme, parameters);
            return true;
        }
        catch (JsonException)
        {
            error = "Authentication parameters must be valid JSON for an object of string values.";
            return false;
        }
    }
}
