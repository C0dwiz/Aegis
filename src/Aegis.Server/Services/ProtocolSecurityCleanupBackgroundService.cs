using Aegis.Common;
using Aegis.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Aegis.Server.Services;

/// <summary>
/// Periodically removes expired protocol-security artifacts from the database
/// and enforces TTL hygiene for Redis keys used by replay/auth/csrf flows.
/// </summary>
public sealed class ProtocolSecurityCleanupBackgroundService : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan JitterWindow = TimeSpan.FromMinutes(3);
    private static readonly string[] RedisPatterns =
    {
        "aegis:hs:v2:replay:*",
        "aegis:auth:login:*",
        "aegis:auth:register:*",
        "aegis:csrf:*"
    };

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ProtocolSecurityCleanupBackgroundService> _logger;
    private readonly HealthCheckService _healthCheckService;
    private readonly IConnectionMultiplexer? _redis;
    private readonly Random _random = new(Environment.TickCount);

    public ProtocolSecurityCleanupBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<ProtocolSecurityCleanupBackgroundService> logger,
        HealthCheckService healthCheckService,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _healthCheckService = healthCheckService;

        var redisConnectionString = configuration["Redis:ConnectionString"];
        if (string.IsNullOrWhiteSpace(redisConnectionString))
        {
            return;
        }

        try
        {
            _redis = ConnectionMultiplexer.Connect(redisConnectionString);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Protocol security cleanup Redis integration is disabled");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Protocol security cleanup background service started");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var jittered = TimeSpan.FromMilliseconds(
                        CleanupInterval.TotalMilliseconds +
                        (_random.NextDouble() - 0.5) * JitterWindow.TotalMilliseconds * 2);

                    await Task.Delay(jittered, stoppingToken);
                    if (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }

                    await CleanupOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Protocol security cleanup iteration failed");
                }
            }
        }
        finally
        {
            _redis?.Dispose();
            _logger.LogInformation("Protocol security cleanup background service stopped");
        }
    }

    private async Task CleanupOnceAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var replayDeleted = 0;
        var saltDeleted = 0;

        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AegisDbContext>();
            replayDeleted = await db.HandshakeReplayEntries
                .Where(x => x.ExpiresAt < now)
                .ExecuteDeleteAsync(cancellationToken);

            var staleSaltCutoff = now.AddDays(-1);
            saltDeleted = await db.SessionSaltStates
                .Where(x => (!x.IsActive && x.RotatedAt < staleSaltCutoff) ||
                            (x.PreviousSaltValidUntil != null && x.PreviousSaltValidUntil < now))
                .ExecuteDeleteAsync(cancellationToken);
        }

        var (redisScanned, redisDeleted) = await CleanupRedisKeysWithoutTtlAsync();

        _healthCheckService.RecordProtocolCleanupRun(
            replayDeleted,
            saltDeleted,
            redisScanned,
            redisDeleted);

        _logger.LogInformation(
            "Protocol cleanup completed: replayDeleted={ReplayDeleted}, saltDeleted={SaltDeleted}, redisScanned={RedisScanned}, redisDeleted={RedisDeleted}",
            replayDeleted,
            saltDeleted,
            redisScanned,
            redisDeleted);
    }

    private Task<(int scanned, int deleted)> CleanupRedisKeysWithoutTtlAsync()
    {
        if (_redis == null)
        {
            return Task.FromResult((0, 0));
        }

        var scanned = 0;
        var deleted = 0;

        try
        {
            foreach (var endpoint in _redis.GetEndPoints())
            {
                var server = _redis.GetServer(endpoint);
                if (!server.IsConnected)
                {
                    continue;
                }

                var db = _redis.GetDatabase();
                foreach (var pattern in RedisPatterns)
                {
                    foreach (var key in server.Keys(pattern: pattern, pageSize: 500))
                    {
                        scanned++;
                        var ttl = db.KeyTimeToLive(key);
                        if (ttl == null)
                        {
                            if (db.KeyDelete(key))
                            {
                                deleted++;
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis key hygiene pass failed; will retry on next cycle");
        }

        return Task.FromResult((scanned, deleted));
    }
}
