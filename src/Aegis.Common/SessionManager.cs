using Aegis.Common.Logging;
using System.Collections.Concurrent;

namespace Aegis.Common;

public class SessionManager : IDisposable
{
    private readonly ConcurrentDictionary<ulong, SessionInfo> _sessions;

    // userId → set of active connectionIds (one per device)
    // Using ConcurrentDictionary<ulong, byte> as a thread-safe HashSet.
    private readonly ConcurrentDictionary<ulong, ConcurrentDictionary<ulong, byte>> _userConnections;
    private readonly ConcurrentDictionary<ulong, bool> _userPresenceState;

    // Fast O(1) set of online userIds — updated in AuthenticateSession / RemoveSession.
    private readonly ConcurrentDictionary<ulong, byte> _onlineUserIds;

    private readonly ISessionCryptoProvider _cryptoProvider;
    private readonly ILogger _logger;
    private readonly Timer _zombieCleanupTimer;

    /// <summary>
    /// How long a session can be idle in-memory before being considered a zombie and removed.
    /// Default: 10 minutes (server's own idle TCP timeout is usually ~5 min, so this is a safety net).
    /// </summary>
    private static readonly TimeSpan ZombieTimeout = TimeSpan.FromMinutes(10);

    public SessionManager(ISessionCryptoProvider cryptoProvider, ILogger logger)
    {
        _sessions = new ConcurrentDictionary<ulong, SessionInfo>();
        _userConnections = new ConcurrentDictionary<ulong, ConcurrentDictionary<ulong, byte>>();
        _userPresenceState = new ConcurrentDictionary<ulong, bool>();
        _onlineUserIds = new ConcurrentDictionary<ulong, byte>();
        _cryptoProvider = cryptoProvider;
        _logger = logger;

        // Zombie cleanup: runs every 5 minutes.
        _zombieCleanupTimer = new Timer(CleanupZombieSessions, null,
            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    private void CleanupZombieSessions(object? state)
    {
        var cutoff = DateTime.UtcNow - ZombieTimeout;
        var zombies = _sessions.Values
            .Where(s => s.LastActivity < cutoff)
            .Select(s => s.ConnectionId)
            .ToList();

        if (zombies.Count == 0) return;

        _logger.Warning($"Removing {zombies.Count} zombie session(s) with no activity since {cutoff:O}");
        foreach (var connId in zombies)
            RemoveSession(connId);
    }

    public SessionInfo CreateSession(ulong connectionId)
    {
        var session = new SessionInfo
        {
            ConnectionId = connectionId,
            CreatedAt = DateTime.UtcNow,
            LastActivity = DateTime.UtcNow,
            SessionKey = Memory<byte>.Empty,
            HandshakeEstablished = false
        };

        _sessions.TryAdd(connectionId, session);
        _logger.Info($"Session created for connection {connectionId}");

        return session;
    }

    public SessionInfo? GetSession(ulong connectionId)
    {
        _sessions.TryGetValue(connectionId, out var session);
        return session;
    }

    public void UpdateActivity(ulong connectionId)
    {
        if (_sessions.TryGetValue(connectionId, out var session))
        {
            session.LastActivity = DateTime.UtcNow;
        }
    }

    public bool AuthenticateSession(ulong connectionId, ulong userId, string username)
    {
        if (_sessions.TryGetValue(connectionId, out var session))
        {
            if (!session.HandshakeEstablished)
            {
                _logger.Warning($"Rejected authentication before handshake for connection {connectionId}");
                return false;
            }

            session.UserId = userId;
            session.Username = username;
            session.IsAuthenticated = true;

            // Register this connection in the per-user set
            var connSet = _userConnections.GetOrAdd(userId, _ => new ConcurrentDictionary<ulong, byte>());
            connSet.TryAdd(connectionId, 0);

            _userPresenceState.AddOrUpdate(userId, true, (_, _) => true);
            _onlineUserIds.TryAdd(userId, 0);
            _logger.Info($"Session authenticated for connection {connectionId}, user {username} (ID: {userId}), active connections: {connSet.Count}");
            return true;
        }
        return false;
    }

    /// <summary>Returns one arbitrary active connection for backward compatibility.</summary>
    public bool TryGetConnectionIdByUserId(ulong userId, out ulong connectionId)
    {
        if (_userConnections.TryGetValue(userId, out var connSet))
        {
            foreach (var kv in connSet)
            {
                connectionId = kv.Key;
                return true;
            }
        }
        connectionId = 0;
        return false;
    }

    /// <summary>Returns one arbitrary active connection for backward compatibility.</summary>
    public ulong? GetConnectionIdByUserId(ulong userId)
    {
        if (_userConnections.TryGetValue(userId, out var connSet))
        {
            foreach (var kv in connSet)
                return kv.Key;
        }
        return null;
    }

    /// <summary>Returns ALL active connection IDs for a user (all devices).</summary>
    public IReadOnlyList<ulong> GetConnectionIdsByUserId(ulong userId)
    {
        if (_userConnections.TryGetValue(userId, out var connSet))
            return connSet.Keys.ToArray();
        return Array.Empty<ulong>();
    }

    public bool IsUserOnline(ulong userId)
    {
        if (!_onlineUserIds.ContainsKey(userId)) return false;
        return _userPresenceState.TryGetValue(userId, out var isOnline) && isOnline;
    }

    /// <summary>O(1) snapshot of all online user IDs.</summary>
    public IReadOnlyList<ulong> GetOnlineUserIds()
    {
        return _onlineUserIds.Keys
            .Where(uid => _userPresenceState.TryGetValue(uid, out var online) && online)
            .ToArray();
    }

    public bool SetUserPresence(ulong connectionId, bool isOnline)
    {
        if (!_sessions.TryGetValue(connectionId, out var session) || !session.IsAuthenticated)
            return false;

        _userPresenceState.AddOrUpdate(session.UserId, isOnline, (_, _) => isOnline);
        return true;
    }

    public SessionInfo? GetAuthenticatedSession(ulong connectionId)
    {
        if (_sessions.TryGetValue(connectionId, out var session) && session.IsAuthenticated)
            return session;
        return null;
    }

    public void RemoveSession(ulong connectionId)
    {
        if (_sessions.TryRemove(connectionId, out var session))
        {
            if (session.IsAuthenticated)
            {
                if (_userConnections.TryGetValue(session.UserId, out var connSet))
                {
                    connSet.TryRemove(connectionId, out _);

                    // Only clean up user-level state when the last connection is gone.
                    if (connSet.IsEmpty)
                    {
                        _userConnections.TryRemove(session.UserId, out _);
                        _userPresenceState.TryRemove(session.UserId, out _);
                        _onlineUserIds.TryRemove(session.UserId, out _);
                    }
                }
            }

            ZeroSessionSecrets(session);
            _logger.Info($"Session removed for connection {connectionId}");
        }
    }

    public bool EstablishHandshake(ulong connectionId, ReadOnlySpan<byte> sessionKey)
    {
        if (!_sessions.TryGetValue(connectionId, out var session))
            return false;

        ZeroSessionSecrets(session);

        session.SessionKey = sessionKey.ToArray();
        session.HandshakeEstablished = true;
        session.LastActivity = DateTime.UtcNow;

        _logger.Info($"Handshake established for connection {connectionId}");
        return true;
    }

    private static void ZeroSessionSecrets(SessionInfo session)
    {
        if (!session.SessionKey.IsEmpty)
            session.SessionKey.Span.Clear();

        session.SessionKey = Memory<byte>.Empty;
        session.HandshakeEstablished = false;
    }

    public void Dispose() => _zombieCleanupTimer.Dispose();
}

public class SessionInfo
{
    public ulong ConnectionId { get; set; }
    public ulong UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public bool IsAuthenticated { get; set; }
    public bool HandshakeEstablished { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastActivity { get; set; }
    public Memory<byte> SessionKey { get; set; }
}
