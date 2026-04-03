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
        var totpEncryptionKey = configuration["Security:TotpEncryptionKey"] ?? string.Empty;

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

        if (environment.IsProduction())
        {
            ValidateTotpEncryptionKey(totpEncryptionKey);
        }
    }

    private static void ValidateTotpEncryptionKey(string keyValue)
    {
        if (string.IsNullOrWhiteSpace(keyValue))
        {
            throw new InvalidOperationException("Security:TotpEncryptionKey must be configured in Bot API production config.");
        }

        byte[] raw;
        try
        {
            raw = Convert.FromBase64String(keyValue);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("Security:TotpEncryptionKey must be Base64-encoded.", ex);
        }

        if (raw.Length != 32)
        {
            throw new InvalidOperationException("Security:TotpEncryptionKey must decode to exactly 32 bytes.");
        }
    }
}
