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

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute))
        {
            return absolute;
        }

        var scheme = useSslByDefault ? "https" : "http";
        if (Uri.TryCreate($"{scheme}://{trimmed}", UriKind.Absolute, out absolute))
        {
            return absolute;
        }

        throw new InvalidOperationException($"Invalid dependency endpoint format: {endpoint}");
    }
}
