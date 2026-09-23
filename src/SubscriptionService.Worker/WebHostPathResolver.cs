namespace SubscriptionService.Worker;

public static class WebHostPathResolver
{
    public static string ResolveWebRoot(bool isWindowsService, string contentRoot, string executableDirectory)
    {
        var root = isWindowsService ? executableDirectory : contentRoot;
        return Path.Combine(root, "wwwroot");
    }
}
