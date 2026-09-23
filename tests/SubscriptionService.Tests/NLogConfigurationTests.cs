namespace SubscriptionService.Tests;

public sealed class NLogConfigurationTests
{
    [Fact]
    public void NLogConfiguration_DefinesConsoleAndRollingFileTargets()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../src/SubscriptionService.Worker/nlog.config"));

        Assert.True(File.Exists(path), $"Expected NLog configuration at {path}");

        var configuration = File.ReadAllText(path);
        Assert.Contains("name=\"console\"", configuration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("name=\"rollingFile\"", configuration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("logs/catchup-service-${shortdate}.log", configuration, StringComparison.OrdinalIgnoreCase);
    }
}
