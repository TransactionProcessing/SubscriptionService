namespace CatchupService.Domain;

public sealed record RetrySettings(int MaxAttempts, TimeSpan Delay)
{
    public static RetrySettings Default { get; } = new(3, TimeSpan.FromSeconds(1));
}

public sealed record CheckpointSettings(int BatchSize)
{
    public static CheckpointSettings Default { get; } = new(100);
}

public sealed record TimeoutSettings(TimeSpan RequestTimeout, TimeSpan PollInterval)
{
    public static TimeoutSettings Default { get; } = new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));
}

public sealed record AuthenticationConfiguration(string? Scheme, IReadOnlyDictionary<string, string> Parameters)
{
    public static AuthenticationConfiguration Empty { get; } = new(null, new Dictionary<string, string>());
}
