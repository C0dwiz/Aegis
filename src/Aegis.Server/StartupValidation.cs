using Aegis.Common.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Aegis.Server;

internal static class StartupValidation
{
    public static void ValidateServerConfiguration(IConfiguration configuration, IHostEnvironment environment)
    {
        var dbConnectionString = configuration[$"{DatabaseOptions.SectionName}:ConnectionString"] ?? string.Empty;
        var redisConnectionString = configuration["Redis:ConnectionString"] ?? string.Empty;
        var requireEncryptedAfterHandshake = configuration.GetValue<bool>($"{ProtocolSecurityOptions.SectionName}:RequireEncryptedPayloadAfterHandshake");
        var enableTransportMasking = configuration.GetValue<bool>($"{ServerOptions.SectionName}:EnableTransportMasking");
        var transportMaskingKey = configuration[$"{ServerOptions.SectionName}:TransportMaskingKey"] ?? string.Empty;

        if (string.IsNullOrWhiteSpace(dbConnectionString))
        {
            throw new InvalidOperationException("Database connection string is required for server startup.");
        }

        if (string.IsNullOrWhiteSpace(redisConnectionString))
        {
            throw new InvalidOperationException("Redis connection string is required for server startup.");
        }

        if (enableTransportMasking && string.IsNullOrWhiteSpace(transportMaskingKey))
        {
            throw new InvalidOperationException("Server:TransportMaskingKey must be set when Server:EnableTransportMasking=true.");
        }

        if (environment.IsProduction())
        {
            if (!requireEncryptedAfterHandshake)
            {
                throw new InvalidOperationException("ProtocolSecurity:RequireEncryptedPayloadAfterHandshake must be enabled in production.");
            }

            if (dbConnectionString.Contains("Password=aegis", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Default database password is not allowed in production.");
            }
        }
    }

    public static void ValidateBotApiConfiguration(IConfiguration configuration, IHostEnvironment environment)
    {
        var dbConnectionString = configuration.GetConnectionString("Default")
            ?? configuration[$"{DatabaseOptions.SectionName}:ConnectionString"]
            ?? string.Empty;
        var redisConnectionString = configuration["Redis:ConnectionString"] ?? string.Empty;

        if (string.IsNullOrWhiteSpace(dbConnectionString))
        {
            throw new InvalidOperationException("Bot API database connection string is required.");
        }

        if (string.IsNullOrWhiteSpace(redisConnectionString))
        {
            throw new InvalidOperationException("Bot API Redis connection string is required.");
        }

        if (environment.IsProduction() && dbConnectionString.Contains("Password=aegis", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Default database password is not allowed in Bot API production config.");
        }
    }
}
