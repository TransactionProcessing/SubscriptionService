namespace CatchupService.Application;

public sealed record DeliveryOutcome(bool IsSuccess, string? FailureReason = null)
{
    public static DeliveryOutcome Success { get; } = new(true);

    public static DeliveryOutcome Failure(string reason) => new(false, reason);
}
