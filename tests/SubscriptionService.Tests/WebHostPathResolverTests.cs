using SubscriptionService.Worker;

namespace SubscriptionService.Tests;

public sealed class WebHostPathResolverTests
{
    [Fact]
    public void ResolveWebRoot_UsesExecutableDirectoryForWindowsService()
    {
        var result = WebHostPathResolver.ResolveWebRoot(
            isWindowsService: true,
            contentRoot: "C:\\Windows\\System32",
            executableDirectory: "C:\\Program Files\\CatchupService");

        Assert.Equal("C:\\Program Files\\CatchupService\\wwwroot", result);
    }

    [Fact]
    public void ResolveWebRoot_PreservesInteractiveContentRoot()
    {
        var result = WebHostPathResolver.ResolveWebRoot(
            isWindowsService: false,
            contentRoot: "C:\\dev\\CatchupService",
            executableDirectory: "C:\\Program Files\\CatchupService");

        Assert.Equal(Path.Combine("C:\\dev\\CatchupService", "wwwroot"), result);
    }
}
