namespace Aegis.Common;

public interface IRateLimiter
{
    bool CanConnect(string ipAddress);
    void RegisterConnection(ulong connectionId, string ipAddress);
    void RecordDisconnection(string ipAddress);
    bool CanSendAuthRequest(ulong connectionId);
    bool CanSendMessage(ulong connectionId);
    void RemoveConnection(ulong connectionId);
}
