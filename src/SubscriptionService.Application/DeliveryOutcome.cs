namespace SubscriptionService.Application;

public sealed record DeliveryOutcome(
    bool IsSuccess,
    string? FailureReason = null,
    int? ResponseStatusCode = null,
    string? ResponseBody = null)
{
    public static DeliveryOutcome Success { get; } = new(true);

    public static DeliveryOutcome Failure(string reason, int? responseStatusCode = null, string? responseBody = null) =>
        new(false, reason, responseStatusCode, responseBody);
}
