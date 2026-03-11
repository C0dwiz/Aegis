using Aegis.Common.Configuration;

namespace Aegis.BotApi;

internal static class BotApiStartupValidation
{
    public static void Validate(IConfiguration configuration, IHostEnvironment environment)
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
