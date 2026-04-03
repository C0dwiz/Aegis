using Aegis.Common.Configuration;
using Aegis.Data.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;
using StackExchange.Redis;

namespace Aegis.Server;

internal static class StartupValidation
{
    public static void ValidateServerConfiguration(IConfiguration configuration, IHostEnvironment environment)
    {
        var dbConnectionString = configuration[$"{DatabaseOptions.SectionName}:ConnectionString"] ?? string.Empty;
        var redisConnectionString = configuration["Redis:ConnectionString"] ?? string.Empty;
        var requireEncryptedAfterHandshake = configuration.GetValue<bool>($"{ProtocolSecurityOptions.SectionName}:RequireEncryptedPayloadAfterHandshake");
        var requireSignedHandshakeResponses = configuration.GetValue<bool>($"{ProtocolSecurityOptions.SectionName}:RequireSignedHandshakeResponses");
        var handshakeSigningPrivateKey = configuration[$"{ProtocolSecurityOptions.SectionName}:HandshakeSigningPrivateKeyBase64"] ?? string.Empty;
        var enableV2Handshake = configuration.GetValue<bool>($"{ProtocolSecurityOptions.SectionName}:EnableV2Handshake");
        var v2ClockSkew = configuration.GetValue<long>($"{ProtocolSecurityOptions.SectionName}:V2HandshakeClockSkewMs");
        var v2CookieTtl = configuration.GetValue<long>($"{ProtocolSecurityOptions.SectionName}:V2HandshakeCookieTtlMs");
        var v2ReplayWindow = configuration.GetValue<int>($"{ProtocolSecurityOptions.SectionName}:V2ReplayWindowSeconds");
        var enableTransportMasking = configuration.GetValue<bool>($"{ServerOptions.SectionName}:EnableTransportMasking");
        var transportMaskingKey = configuration[$"{ServerOptions.SectionName}:TransportMaskingKey"] ?? string.Empty;
        var totpEncryptionKey = configuration["Security:TotpEncryptionKey"] ?? string.Empty;

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

            ValidateTotpEncryptionKey(totpEncryptionKey, "Security:TotpEncryptionKey");
        }

        if (requireSignedHandshakeResponses && string.IsNullOrWhiteSpace(handshakeSigningPrivateKey))
        {
            throw new InvalidOperationException("ProtocolSecurity:HandshakeSigningPrivateKeyBase64 is required when signed handshake responses are enabled.");
        }

        if (enableV2Handshake)
        {
            if (v2ClockSkew <= 0 || v2ClockSkew > 300_000)
            {
                throw new InvalidOperationException("ProtocolSecurity:V2HandshakeClockSkewMs must be in range 1..300000.");
            }

            if (v2CookieTtl < 5_000 || v2CookieTtl > 300_000)
            {
                throw new InvalidOperationException("ProtocolSecurity:V2HandshakeCookieTtlMs must be in range 5000..300000.");
            }

            if (v2ReplayWindow < 30 || v2ReplayWindow > 3600)
            {
                throw new InvalidOperationException("ProtocolSecurity:V2ReplayWindowSeconds must be in range 30..3600.");
            }
        }

        ValidateServerDependencies(configuration);
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

        ValidateCommonDependencies(configuration, dbConnectionString, redisConnectionString);
    }

    private static void ValidateServerDependencies(IConfiguration configuration)
    {
        var dbConnectionString = configuration[$"{DatabaseOptions.SectionName}:ConnectionString"] ?? string.Empty;
        var redisConnectionString = configuration["Redis:ConnectionString"] ?? string.Empty;

        ValidateCommonDependencies(configuration, dbConnectionString, redisConnectionString);
    }

    private static void ValidateCommonDependencies(IConfiguration configuration, string dbConnectionString, string redisConnectionString)
    {
        ValidatePostgresConnectivity(dbConnectionString);
        ValidateRedisConnectivity(redisConnectionString);

        var avatarProvider = configuration[$"{AvatarStorageOptions.SectionName}:Provider"] ?? string.Empty;
        var minioEnabled = configuration.GetValue<bool>($"{MinioStorageOptions.SectionName}:Enabled")
            || string.Equals(avatarProvider, "MinIO", StringComparison.OrdinalIgnoreCase);
        if (minioEnabled)
        {
            var minioEndpoint = configuration[$"{MinioStorageOptions.SectionName}:Endpoint"] ?? string.Empty;
            var minioUseSsl = configuration.GetValue<bool>($"{MinioStorageOptions.SectionName}:UseSsl");
            ValidateHttpDependency("MinIO", BuildEndpoint(minioEndpoint, minioUseSsl), "/minio/health/live");
        }

        var elasticEnabled = configuration.GetValue<bool>($"{ElasticsearchOptions.SectionName}:Enabled");
        if (elasticEnabled)
        {
            var elasticEndpoint = configuration[$"{ElasticsearchOptions.SectionName}:Endpoint"] ?? string.Empty;
            ValidateHttpDependency("Elasticsearch", BuildEndpoint(elasticEndpoint, useSslByDefault: false), "/_cluster/health");
        }
    }

    private static void ValidatePostgresConnectivity(string connectionString)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString)
            {
                Timeout = 3,
                CommandTimeout = 3
            };

            using var connection = new NpgsqlConnection(builder.ConnectionString);
            connection.Open();
            connection.Close();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Database dependency check failed: PostgreSQL is not reachable.", ex);
        }
    }

    private static void ValidateRedisConnectivity(string connectionString)
    {
        try
        {
            var options = ConfigurationOptions.Parse(connectionString);
            options.AbortOnConnectFail = true;
            options.ConnectTimeout = 3000;
            options.SyncTimeout = 3000;

            using var multiplexer = ConnectionMultiplexer.Connect(options);
            _ = multiplexer.GetDatabase().Ping();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Dependency check failed: Redis is not reachable.", ex);
        }
    }

    private static void ValidateHttpDependency(string dependencyName, Uri endpoint, string probePath)
    {
        try
        {
            using var client = new HttpClient
            {
                BaseAddress = endpoint,
                Timeout = TimeSpan.FromSeconds(3)
            };

            using var response = client.GetAsync(probePath).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"{dependencyName} responded with status {(int)response.StatusCode}.");
            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"Dependency check failed: {dependencyName} is not reachable at {endpoint}.", ex);
        }
    }

    private static Uri BuildEndpoint(string endpoint, bool useSslByDefault)
    {
        var trimmed = endpoint?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new InvalidOperationException("Dependency endpoint is missing.");
        }

        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var explicitHttpUri))
            {
                return explicitHttpUri;
            }

            throw new InvalidOperationException($"Invalid dependency endpoint format: {endpoint}");
        }

        // Values like "minio:9000" or "elasticsearch:9200" must be treated as host:port,
        // not as a custom URI scheme.
        if (trimmed.Contains("://", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unsupported dependency endpoint scheme: {endpoint}");
        }

        var scheme = useSslByDefault ? "https" : "http";
        if (Uri.TryCreate($"{scheme}://{trimmed}", UriKind.Absolute, out var inferredUri))
        {
            return inferredUri;
        }

        throw new InvalidOperationException($"Invalid dependency endpoint format: {endpoint}");
    }

    private static void ValidateTotpEncryptionKey(string keyValue, string settingName)
    {
        if (string.IsNullOrWhiteSpace(keyValue))
        {
            throw new InvalidOperationException($"{settingName} must be configured in production.");
        }

        byte[] raw;
        try
        {
            raw = Convert.FromBase64String(keyValue);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException($"{settingName} must be Base64-encoded.", ex);
        }

        if (raw.Length != 32)
        {
            throw new InvalidOperationException($"{settingName} must decode to exactly 32 bytes.");
        }
    }
}
