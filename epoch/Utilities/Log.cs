using Microsoft.Extensions.Logging;

namespace epoch.Utilities.Logging;

public static class Log
{
    private static ILoggerFactory? _factory;
    private static ILogger? _logger;

    private static int count = 0;

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

    public static void Limited(string message, params object[] args)
    {
        if (count < 50)
        {
            _logger?.LogDebug(message, args);
            count++;
        }
    }
}
