using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace epoch.Utilities.Logging;

/// <summary>
/// Thin static facade over <see cref="ILogger"/>. Call <see cref="Initialize"/> once at startup
/// to wire up console logging. All methods are null-safe before initialization.
/// </summary>
public static class Log
{
    private static ILoggerFactory? _factory;
    private static ILogger? _logger;

    private static readonly Dictionary<string, int> _limitedCounts = new();

    public static void Initialize()
    {
        _factory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug);
        });

        _logger = _factory.CreateLogger("Global");
    }

    public static void Info(string message, params object[] args) =>
        _logger?.LogInformation(message, args);

    public static void Warn(string message, params object[] args) =>
        _logger?.LogWarning(message, args);

    public static void Error(string message, params object[] args) =>
        _logger?.LogError(message, args);

    public static void Debug(string message, params object[] args) =>
        _logger?.LogDebug(message, args);

    /// <summary>
    /// Logs a debug message, but only the first 50 times per unique message template.
    /// Useful for noisy per-frame warnings that would otherwise flood the console.
    /// </summary>
    public static void Limited(string message, params object[] args)
    {
        _limitedCounts.TryGetValue(message, out int count);
        if (count < 50)
        {
            _logger?.LogDebug(message, args);
            _limitedCounts[message] = count + 1;
        }
    }
}
