using Aegis.Common.Logging;
using System.Collections.Concurrent;

namespace Aegis.Common;

public class SessionManager
{
    private readonly ConcurrentDictionary<ulong, SessionInfo> _sessions;
    private readonly ConcurrentDictionary<ulong, ulong> _userConnections;
    private readonly ConcurrentDictionary<ulong, bool> _userPresenceState;
    private readonly ISessionCryptoProvider _cryptoProvider;
    private readonly ILogger _logger;
    
    public SessionManager(ISessionCryptoProvider cryptoProvider, ILogger logger)
    {
        _sessions = new ConcurrentDictionary<ulong, SessionInfo>();
        _userConnections = new ConcurrentDictionary<ulong, ulong>();
        _userPresenceState = new ConcurrentDictionary<ulong, bool>();
        _cryptoProvider = cryptoProvider;
        _logger = logger;
    }
    
    public SessionInfo CreateSession(ulong connectionId)
    {
        var session = new SessionInfo
        {
            ConnectionId = connectionId,
            CreatedAt = DateTime.UtcNow,
            LastActivity = DateTime.UtcNow,
            SessionKey = Memory<byte>.Empty,
            MacKey = Memory<byte>.Empty,
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
            _userConnections.AddOrUpdate(userId, connectionId, (_, _) => connectionId);
            _userPresenceState.AddOrUpdate(userId, true, (_, _) => true);
            _logger.Info($"Session authenticated for connection {connectionId}, user {username} (ID: {userId})");
            return true;
        }
        return false;
    }

    public bool TryGetConnectionIdByUserId(ulong userId, out ulong connectionId)
    {
        return _userConnections.TryGetValue(userId, out connectionId);
    }

    public ulong? GetConnectionIdByUserId(ulong userId)
    {
        return _userConnections.TryGetValue(userId, out var connectionId) ? connectionId : null;
    }

    public bool IsUserOnline(ulong userId)
    {
        if (!_userConnections.TryGetValue(userId, out _))
        {
            return false;
        }

        if (_userPresenceState.TryGetValue(userId, out var isOnline))
        {
            return isOnline;
        }

        return true;
    }

    public bool SetUserPresence(ulong connectionId, bool isOnline)
    {
        if (!_sessions.TryGetValue(connectionId, out var session) || !session.IsAuthenticated)
        {
            return false;
        }

        _userPresenceState.AddOrUpdate(session.UserId, isOnline, (_, _) => isOnline);
        return true;
    }
    
    public SessionInfo? GetAuthenticatedSession(ulong connectionId)
    {
        if (_sessions.TryGetValue(connectionId, out var session) && session.IsAuthenticated)
        {
            return session;
        }
        return null;
    }
    
    public void RemoveSession(ulong connectionId)
    {
        if (_sessions.TryRemove(connectionId, out var session))
        {
            if (session.IsAuthenticated)
            {
                if (_userConnections.TryGetValue(session.UserId, out var mappedConnection) && mappedConnection == connectionId)
                {
                    _userConnections.TryRemove(session.UserId, out _);
                }

                _userPresenceState.TryRemove(session.UserId, out _);
            }

            ZeroSessionSecrets(session);
            _logger.Info($"Session removed for connection {connectionId}");
        }
    }

    public bool EstablishHandshake(ulong connectionId, ReadOnlySpan<byte> sessionKey, ReadOnlySpan<byte> macKey)
    {
        if (!_sessions.TryGetValue(connectionId, out var session))
        {
            return false;
        }

        ZeroSessionSecrets(session);

        session.SessionKey = sessionKey.ToArray();
        session.MacKey = macKey.ToArray();
        session.HandshakeEstablished = true;
        session.LastActivity = DateTime.UtcNow;

        _logger.Info($"Handshake established for connection {connectionId}");
        return true;
    }

    public bool CanValidateMac(ulong connectionId)
    {
        return _sessions.TryGetValue(connectionId, out var session) &&
            session.HandshakeEstablished &&
            !session.MacKey.IsEmpty;
    }
    
    public bool VerifyMac(ulong connectionId, ReadOnlySpan<byte> data, ReadOnlySpan<byte> receivedMac)
    {
        var session = GetSession(connectionId);
        if (session == null || !session.HandshakeEstablished || session.MacKey.IsEmpty)
        {
            _logger.Warning($"No session found for connection {connectionId}");
            return false;
        }
        
        return _cryptoProvider.VerifyMac(data, session.MacKey.Span, receivedMac);
    }

    private static void ZeroSessionSecrets(SessionInfo session)
    {
        if (!session.SessionKey.IsEmpty)
        {
            session.SessionKey.Span.Clear();
        }

        if (!session.MacKey.IsEmpty)
        {
            session.MacKey.Span.Clear();
        }

        session.SessionKey = Memory<byte>.Empty;
        session.MacKey = Memory<byte>.Empty;
        session.HandshakeEstablished = false;
    }
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
    public Memory<byte> MacKey { get; set; }
}
