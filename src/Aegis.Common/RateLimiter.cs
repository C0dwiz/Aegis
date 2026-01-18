using System.Collections.Concurrent;
using System.Net;
using Aegis.Common.Configuration;
using Aegis.Common.Logging;

namespace Aegis.Common;

/// <summary>
/// Rate limiter for protecting against spam and DoS attacks
/// </summary>
public class RateLimiter
{
    private readonly ConcurrentDictionary<string, ConnectionRateLimit> _ipRateLimits;
    private readonly ConcurrentDictionary<ulong, MessageRateLimit> _connectionRateLimits;
    private readonly RateLimitOptions _options;
    private readonly ILogger _logger;
    private Timer? _cleanupTimer;

    public RateLimiter(RateLimitOptions options, ILogger? logger = null)
    {
        _ipRateLimits = new ConcurrentDictionary<string, ConnectionRateLimit>();
        _connectionRateLimits = new ConcurrentDictionary<ulong, MessageRateLimit>();
        _options = options;
        _logger = logger ?? new NullLogger();

        _cleanupTimer = new Timer(CleanupExpiredEntries, null,
            TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
    }

    /// <summary>
    /// Check if IP has exceeded connection limit
    /// </summary>
    public bool CanConnect(string ipAddress)
    {
        var limit = _ipRateLimits.AddOrUpdate(ipAddress,
            _ => new ConnectionRateLimit(),
            (_, existing) =>
            {
                // Reset if older than 1 minute
                if (DateTime.UtcNow - existing.LastReset > TimeSpan.FromMinutes(1))
                {
                    return new ConnectionRateLimit();
                }
                return existing;
            });

        if (limit.ConnectionCount >= _options.MaxConnectionsPerIP)
        {
            _logger.Warning($"IP {ipAddress} exceeded max connections ({_options.MaxConnectionsPerIP})");
            return false;
        }

        limit.ConnectionCount++;
        return true;
    }

    /// <summary>
    /// Record IP disconnection
    /// </summary>
    public void RecordDisconnection(string ipAddress)
    {
        if (_ipRateLimits.TryGetValue(ipAddress, out var limit))
        {
            limit.ConnectionCount = Math.Max(0, limit.ConnectionCount - 1);
        }
    }

    /// <summary>
    /// Check if connection can send auth request
    /// </summary>
    public bool CanSendAuthRequest(ulong connectionId)
    {
        var now = DateTime.UtcNow;
        var limit = _connectionRateLimits.AddOrUpdate(connectionId,
            _ => new MessageRateLimit { LastReset = now },
            (_, existing) =>
            {
                // Reset counter every minute
                if (now - existing.LastReset > TimeSpan.FromMinutes(1))
                {
                    existing.AuthAttempts = 0;
                    existing.LastReset = now;
                }
                return existing;
            });

        if (limit.AuthAttempts >= _options.MaxAuthAttemptsPerMinute)
        {
            _logger.Warning($"Connection {connectionId} exceeded max auth attempts");
            return false;
        }

        limit.AuthAttempts++;
        return true;
    }

    /// <summary>
    /// Check if connection can send message
    /// </summary>
    public bool CanSendMessage(ulong connectionId)
    {
        var now = DateTime.UtcNow;
        var limit = _connectionRateLimits.AddOrUpdate(connectionId,
            _ => new MessageRateLimit { LastReset = now },
            (_, existing) =>
            {
                // Reset counter every second
                if (now - existing.LastMessageReset > TimeSpan.FromSeconds(1))
                {
                    existing.MessageCount = 0;
                    existing.LastMessageReset = now;
                }
                return existing;
            });

        if (limit.MessageCount >= _options.MaxMessagesPerSecond)
        {
            _logger.Warning($"Connection {connectionId} exceeded max messages per second");
            return false;
        }

        limit.MessageCount++;
        return true;
    }

    /// <summary>
    /// Remove rate limit entry for connection
    /// </summary>
    public void RemoveConnection(ulong connectionId)
    {
        _connectionRateLimits.TryRemove(connectionId, out _);
    }

    private void CleanupExpiredEntries(object? state)
    {
        var now = DateTime.UtcNow;
        var maxAge = TimeSpan.FromHours(1);

        // Cleanup connection rate limits
        var toRemoveConnections = _connectionRateLimits
            .Where(kvp => now - kvp.Value.LastReset > maxAge)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var connId in toRemoveConnections)
        {
            _connectionRateLimits.TryRemove(connId, out _);
        }

        // Cleanup IP rate limits (older than 1 hour with no activity)
        var toRemoveIps = _ipRateLimits
            .Where(kvp => now - kvp.Value.LastReset > maxAge)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var ip in toRemoveIps)
        {
            _ipRateLimits.TryRemove(ip, out _);
        }

        if (toRemoveConnections.Count > 0 || toRemoveIps.Count > 0)
        {
            _logger.Info($"Cleaned up {toRemoveConnections.Count} connection limits and {toRemoveIps.Count} IP limits");
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
    }
}

/// <summary>
/// Tracks rate limit info per IP address
/// </summary>
internal class ConnectionRateLimit
{
    public int ConnectionCount { get; set; } = 1;
    public DateTime LastReset { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Tracks rate limit info per connection
/// </summary>
internal class MessageRateLimit
{
    public int AuthAttempts { get; set; }
    public int MessageCount { get; set; }
    public DateTime LastReset { get; set; } = DateTime.UtcNow;
    public DateTime LastMessageReset { get; set; } = DateTime.UtcNow;
}
