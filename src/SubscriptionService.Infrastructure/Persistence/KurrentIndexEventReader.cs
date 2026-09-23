using KurrentDB.Client;
using SubscriptionService.Application;

namespace SubscriptionService.Infrastructure.Persistence;

public sealed class KurrentIndexEventReader : IIndexEventReader
{
    private readonly KurrentDBClient _client;

    public KurrentIndexEventReader(KurrentDBClient client)
    {
        this._client = client;
    }

    public async Task<IndexEventCountPage> ReadNextPageAsync(
        string secondaryIndexName,
        long? afterCommitPosition,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        var startPosition = afterCommitPosition.HasValue
            ? new Position((ulong)afterCommitPosition.Value, (ulong)afterCommitPosition.Value)
            : Position.Start;

        var read = this._client.ReadAllAsync(
            Direction.Forwards,
            startPosition,
            StreamFilter.Prefix(secondaryIndexName),
            maxCount,
            false,
            null,
            null,
            cancellationToken);

        var commitPositions = new List<long>(maxCount);
        long? resumeCommitPosition = null;

        await foreach (var message in read.Messages.WithCancellation(cancellationToken))
        {
            switch (message)
            {
                case StreamMessage.Event(var resolvedEvent):
                    commitPositions.Add(unchecked((long)resolvedEvent.OriginalEvent.Position.CommitPosition));
                    break;
                case StreamMessage.LastAllStreamPosition(var position):
                    resumeCommitPosition = unchecked((long)position.CommitPosition);
                    break;
            }
        }

        if (resumeCommitPosition is null && read.LastPosition is { } lastPosition)
        {
            resumeCommitPosition = unchecked((long)lastPosition.CommitPosition);
        }

        return new IndexEventCountPage(commitPositions, resumeCommitPosition);
    }
}
