using Aegis.Common.Logging;
using System.Collections.Concurrent;

namespace Aegis.Common;

public class SessionManager
{
    private readonly ConcurrentDictionary<ulong, SessionInfo> _sessions;
    private readonly ISessionCryptoProvider _cryptoProvider;
    private readonly ILogger _logger;
    
    public SessionManager(ISessionCryptoProvider cryptoProvider, ILogger logger)
    {
        _sessions = new ConcurrentDictionary<ulong, SessionInfo>();
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
            SessionKey = _cryptoProvider.GenerateSessionKey(),
            MacKey = _cryptoProvider.GenerateMacKey()
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
    
    public void RemoveSession(ulong connectionId)
    {
        if (_sessions.TryRemove(connectionId, out var session))
        {
            _logger.Info($"Session removed for connection {connectionId}");
        }
    }
    
    public bool VerifyMac(ulong connectionId, ReadOnlySpan<byte> data, ReadOnlySpan<byte> receivedMac)
    {
        var session = GetSession(connectionId);
        if (session == null)
        {
            _logger.Warning($"No session found for connection {connectionId}");
            return false;
        }
        
        return _cryptoProvider.VerifyMac(data, session.MacKey.Span, receivedMac);
    }
}

public class SessionInfo
{
    public ulong ConnectionId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastActivity { get; set; }
    public Memory<byte> SessionKey { get; set; }
    public Memory<byte> MacKey { get; set; }
}
