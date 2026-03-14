using Aegis.Common;
using Aegis.Common.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Collections.Concurrent;

namespace Aegis.Server.Services;

internal sealed class RedisRateLimiter : IRateLimiter, IDisposable
{
    private readonly RateLimiter _localLimiter;
    private readonly RateLimitOptions _options;
    private readonly ILogger<RedisRateLimiter> _logger;
    private readonly ConcurrentDictionary<ulong, string> _connectionIpMap = new();
    private readonly IConnectionMultiplexer? _redis;
    private readonly IDatabase? _db;

    public RedisRateLimiter(RateLimitOptions options, string? redisConnectionString, ILogger<RedisRateLimiter> logger)
    {
        _options = options;
        _logger = logger;
        _localLimiter = new RateLimiter(options);

        if (string.IsNullOrWhiteSpace(redisConnectionString))
        {
            return;
        }

        try
        {
            _redis = ConnectionMultiplexer.Connect(redisConnectionString);
            _db = _redis.GetDatabase();
            _logger.LogInformation("Redis-backed rate limiter enabled");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize Redis-backed rate limiter, falling back to local mode");
        }
    }

    public bool CanConnect(string ipAddress)
    {
        if (!_localLimiter.CanConnect(ipAddress))
        {
            return false;
        }

        if (_db == null)
        {
            return true;
        }

        return TryIncrementWithinLimit($"rl:ip:{ipAddress}", TimeSpan.FromMinutes(1), _options.MaxConnectionsPerIP);
    }

    public void RecordDisconnection(string ipAddress)
    {
        _localLimiter.RecordDisconnection(ipAddress);

        if (_db == null)
        {
            return;
        }

        try
        {
            _db.StringDecrement($"rl:ip:{ipAddress}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decrement Redis connection counter for {IpAddress}", ipAddress);
        }
    }

    public void RegisterConnection(ulong connectionId, string ipAddress)
    {
        _localLimiter.RegisterConnection(connectionId, ipAddress);

        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return;
        }

        _connectionIpMap[connectionId] = ipAddress;
    }

    public bool CanSendAuthRequest(ulong connectionId)
    {
        if (!_localLimiter.CanSendAuthRequest(connectionId))
        {
            return false;
        }

        if (_db == null)
        {
            return true;
        }

        var scope = ResolveScopeKey(connectionId);
        return TryIncrementWithinLimit($"rl:auth:{scope}", TimeSpan.FromMinutes(1), _options.MaxAuthAttemptsPerMinute);
    }

    public bool CanSendMessage(ulong connectionId)
    {
        if (!_localLimiter.CanSendMessage(connectionId))
        {
            return false;
        }

        if (_db == null)
        {
            return true;
        }

        var scope = ResolveScopeKey(connectionId);
        return TryIncrementWithinLimit($"rl:msg:{scope}", TimeSpan.FromSeconds(1), _options.MaxMessagesPerSecond);
    }

    public void RemoveConnection(ulong connectionId)
    {
        _localLimiter.RemoveConnection(connectionId);
        _connectionIpMap.TryRemove(connectionId, out _);

        if (_db == null)
        {
            return;
        }

        try
        {
            _db.KeyDelete(new RedisKey[]
            {
                $"rl:auth:{connectionId}",
                $"rl:msg:{connectionId}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanup Redis limiter keys for connection {ConnectionId}", connectionId);
        }
    }

    private string ResolveScopeKey(ulong connectionId)
    {
        if (_connectionIpMap.TryGetValue(connectionId, out var ipAddress) && !string.IsNullOrWhiteSpace(ipAddress))
        {
            return $"ip:{ipAddress}";
        }

        return $"conn:{connectionId}";
    }

    public void Dispose()
    {
        _localLimiter.Dispose();
        _redis?.Dispose();
    }

    private bool TryIncrementWithinLimit(string key, TimeSpan ttl, int maxAllowed)
    {
        try
        {
            var current = _db!.StringIncrement(key);
            if (current == 1)
            {
                _db.KeyExpire(key, ttl);
            }

            if (current <= maxAllowed)
            {
                return true;
            }

            _db.StringDecrement(key);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis rate-limit check failed for key {Key}; allowing by distributed layer", key);
            return true;
        }
    }
}
