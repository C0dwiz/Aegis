namespace Aegis.Common;

public interface IRateLimiter
{
    bool CanConnect(string ipAddress);
    void RegisterConnection(ulong connectionId, string ipAddress);
    void RecordDisconnection(string ipAddress);
    bool CanSendAuthRequest(ulong connectionId);
    bool CanSendMessage(ulong connectionId);
    /// <summary>
    /// Shared per-user message rate limit that caps the combined throughput across all
    /// devices of the same user. Call this in addition to (or instead of) the per-connection
    /// check when the authenticated userId is known.
    /// </summary>
    bool CanSendMessageByUser(ulong userId);
    void RemoveConnection(ulong connectionId);
}
