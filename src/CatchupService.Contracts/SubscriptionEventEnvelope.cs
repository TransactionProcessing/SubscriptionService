namespace CatchupService.Contracts;

public sealed record SubscriptionEventEnvelope(
    string SubscriptionName,
    long Position,
    Uri Endpoint,
    byte[] Payload,
    IReadOnlyDictionary<string, string> Headers,
    DateTimeOffset OccurredAtUtc);
