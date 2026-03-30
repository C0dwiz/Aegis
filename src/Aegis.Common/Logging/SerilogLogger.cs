using Serilog;
using Serilog.Core;
using Serilog.Events;
using Aegis.Common.Configuration;

namespace Aegis.Common.Logging;

public class SerilogLogger : ILogger
{
    private readonly global::Serilog.ILogger _logger;

    public SerilogLogger(global::Serilog.ILogger logger)
    {
        _logger = logger;
    }

    public void Debug(string message) => _logger.Debug("{Message}", message);

    public void Info(string message) => _logger.Information("{Message}", message);

    public void Warning(string message) => _logger.Warning("{Message}", message);

    public void Error(string message, Exception? ex = null) =>
        _logger.Error(ex, "{Message}", message);
}

public static class SerilogExtensions
{
    public static global::Serilog.ILogger CreateLogger(LoggingOptions options)
    {
        var loggerConfig = new LoggerConfiguration()
            .MinimumLevel.Is(ParseLogLevel(options.MinimumLevel))
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "AegisMessenger");

        if (options.Console)
        {
            loggerConfig.WriteTo.Console(
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] {Message:lj}{NewLine}{Exception}");
        }

        if (options.File)
        {
            loggerConfig.WriteTo.File(
                options.FilePath,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] {Message:lj}{NewLine}{Exception}");
        }

        return loggerConfig.CreateLogger();
    }

    private static LogEventLevel ParseLogLevel(string level) => level.ToLower() switch
    {
        "debug" => LogEventLevel.Debug,
        "information" or "info" => LogEventLevel.Information,
        "warning" or "warn" => LogEventLevel.Warning,
        "error" => LogEventLevel.Error,
        "fatal" => LogEventLevel.Fatal,
        _ => LogEventLevel.Information
    };
}
