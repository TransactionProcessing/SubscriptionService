using System.Collections.ObjectModel;

namespace CatchupService.Core;

public sealed record SubscriptionEvent(
    string SubscriptionName,
    long Position,
    Uri Endpoint,
    ReadOnlyDictionary<string, string> Headers,
    ReadOnlyMemory<byte> Payload,
    DateTimeOffset OccurredAtUtc,
    bool IsReplay = false);
