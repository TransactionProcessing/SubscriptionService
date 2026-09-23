using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace SubscriptionService.Infrastructure;

public static class SubscriptionLogger
{
    private static ILogger? _logger;

    public static void Initialize(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _logger = loggerFactory.CreateLogger("SubscriptionLogger");
    }

    public static void Reset()
    {
        _logger = null;
    }

    public static void WriteInfo(string message) => Write(TraceEventType.Information, message);

    public static void WriteCritical(string message) => Write(TraceEventType.Critical, message);

    public static void WriteError(string message) => Write(TraceEventType.Error, message);

    public static void WriteVerbose(string message) => Write(TraceEventType.Verbose, message);

    public static void WriteWarning(string message) => Write(TraceEventType.Warning, message);

    public static void WriteCriticalException(Exception exception) =>
        WriteExceptionToLog(TraceEventType.Critical, exception);

    public static void WriteException(Exception exception) =>
        WriteExceptionToLog(TraceEventType.Error, exception);

    public static void WriteExceptionToLog(TraceEventType eventType, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (_logger is null)
        {
            Console.WriteLine($"[{eventType}] {exception}");
            return;
        }

        _logger.Log(MapLogLevel(eventType), exception, "{Message}", exception.Message);
    }

    public static void Write(TraceEventType eventType, string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (_logger is null)
        {
            Console.WriteLine($"[{eventType}] {message}");
            return;
        }

        _logger.Log(MapLogLevel(eventType), "{Message}", message);
    }

    private static LogLevel MapLogLevel(TraceEventType eventType) => eventType switch
    {
        TraceEventType.Critical => LogLevel.Critical,
        TraceEventType.Error => LogLevel.Error,
        TraceEventType.Warning => LogLevel.Warning,
        TraceEventType.Verbose => LogLevel.Trace,
        TraceEventType.Start => LogLevel.Information,
        TraceEventType.Stop => LogLevel.Information,
        TraceEventType.Suspend => LogLevel.Information,
        TraceEventType.Resume => LogLevel.Information,
        TraceEventType.Transfer => LogLevel.Information,
        TraceEventType.Information => LogLevel.Information,
        _ => LogLevel.Information
    };
}
