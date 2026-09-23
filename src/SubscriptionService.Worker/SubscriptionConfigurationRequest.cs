using SubscriptionService.Application;
using SubscriptionService.Domain;

namespace SubscriptionService.Worker;

public sealed record SubscriptionConfigurationMappingResult(
    SubscriptionDefinition? Subscription,
    IReadOnlyDictionary<string, string[]> Errors);

public static class SubscriptionConfigurationRequestMapper
{
    public static SubscriptionConfigurationMappingResult Map(SubscriptionConfigurationRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        AddRequiredError(errors, nameof(request.SubscriptionId), request.SubscriptionId, "SubscriptionId is required.");
        AddRequiredError(errors, nameof(request.SecondaryIndexName), request.SecondaryIndexName, "SecondaryIndexName is required.");
        AddRequiredError(errors, nameof(request.Tag), request.Tag, "Tag is required.");

        if (string.IsNullOrWhiteSpace(request.EndpointUrl))
        {
            errors[nameof(request.EndpointUrl)] = ["EndpointUrl is required."];
        }
        else if (!Uri.TryCreate(request.EndpointUrl, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("http" or "https"))
        {
            errors[nameof(request.EndpointUrl)] = ["EndpointUrl must be an absolute HTTP or HTTPS URL."];
        }

        if (request.TimeoutSeconds <= 0)
            errors[nameof(request.TimeoutSeconds)] = ["TimeoutSeconds must be greater than zero."];
        if (request.RetryMaxAttempts < 0)
            errors[nameof(request.RetryMaxAttempts)] = ["RetryMaxAttempts must be zero or greater."];
        if (request.RetryDelaySeconds < 0)
            errors[nameof(request.RetryDelaySeconds)] = ["RetryDelaySeconds cannot be negative."];
        if (request.CheckpointBatchSize <= 0)
            errors[nameof(request.CheckpointBatchSize)] = ["CheckpointBatchSize must be greater than zero."];

        if (errors.Count != 0)
            return new(null, errors);

        return new(
            new SubscriptionDefinition(
                request.SubscriptionId!.Trim(),
                request.SecondaryIndexName!.Trim(),
                request.EndpointUrl!.Trim(),
                request.Tag!.Trim(),
                new TimeoutSettings(TimeSpan.FromSeconds(request.TimeoutSeconds)),
                new RetrySettings(request.RetryMaxAttempts, TimeSpan.FromSeconds(request.RetryDelaySeconds)),
                new CheckpointSettings(request.CheckpointBatchSize),
                request.ContinueOnParked,
                request.Authentication)
            {
                SoftDeleteParked = request.SoftDeleteParked,
                EnableEventLogging = request.EnableEventLogging
            },
            errors);
    }

    private static void AddRequiredError(
        IDictionary<string, string[]> errors,
        string fieldName,
        string? value,
        string message)
    {
        if (string.IsNullOrWhiteSpace(value))
            errors[fieldName] = [message];
    }
}
