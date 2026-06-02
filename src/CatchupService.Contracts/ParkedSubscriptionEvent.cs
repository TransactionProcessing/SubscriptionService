namespace CatchupService.Contracts;

public sealed record ParkedSubscriptionEvent(
    string SubscriptionName,
    long Position,
    string Reason,
    byte[] Payload,
    IReadOnlyDictionary<string, string> Headers,
    DateTimeOffset ParkedAtUtc);
