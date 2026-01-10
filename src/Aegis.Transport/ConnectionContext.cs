using System.Net.Sockets;
using System.Buffers;

namespace Aegis.Transport;

public class ConnectionContext : IDisposable
{
    private bool _disposed;
    private readonly byte[] _receiveBuffer;
    private readonly byte[] _sendBuffer;
    
    public Socket Socket { get; }
    public ulong ConnectionId { get; }
    public ulong NextSequenceId { get; private set; }
    public DateTime LastActivity { get; private set; }
    
    public ConnectionContext(Socket socket, ulong connectionId, int bufferSize = 8192)
    {
        Socket = socket ?? throw new ArgumentNullException(nameof(socket));
        ConnectionId = connectionId;
        _receiveBuffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        _sendBuffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        LastActivity = DateTime.UtcNow;
    }
    
    public ulong GetNextSequenceId() => ++NextSequenceId;
    
    public Memory<byte> GetReceiveBuffer() => _receiveBuffer;
    public Memory<byte> GetSendBuffer() => _sendBuffer;
    
    public virtual void UpdateActivity() => LastActivity = DateTime.UtcNow;
    
    public void Dispose()
    {
        if (_disposed) return;
        
        try { Socket?.Dispose(); } catch { }
        
        ArrayPool<byte>.Shared.Return(_receiveBuffer);
        ArrayPool<byte>.Shared.Return(_sendBuffer);
        
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
