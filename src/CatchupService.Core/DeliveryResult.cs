namespace CatchupService.Core;

public sealed record DeliveryResult(bool Succeeded, int? StatusCode, string? ErrorMessage)
{
    public static DeliveryResult Success(int statusCode) => new(true, statusCode, null);

    public static DeliveryResult Failure(int? statusCode, string errorMessage) => new(false, statusCode, errorMessage);
}
