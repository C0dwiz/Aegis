using System.Net.Sockets;
using System.Buffers;
using System.Buffers.Binary;
using Aegis.Common.Errors;

namespace Aegis.Transport;

public class ConnectionContext : IDisposable
{
    private const int ProtocolHeaderSize = sizeof(uint) + sizeof(byte) * 3 + sizeof(ushort) + sizeof(ulong) + sizeof(uint);
    private const int ProtocolMacSize = 32;
    private const int ProtocolMaxMessageSize = 1024 * 1024;
    private const int ProtocolMaxPayloadSize = ProtocolMaxMessageSize - ProtocolHeaderSize - ProtocolMacSize;

    private bool _disposed;
    private readonly byte[] _receiveBuffer;
    private readonly byte[] _sendBuffer;
    private byte[] _incomingBuffer;
    private int _incomingLength;
    
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
        _incomingBuffer = ArrayPool<byte>.Shared.Rent(bufferSize * 2);
        _incomingLength = 0;
        LastActivity = DateTime.UtcNow;
    }
    
    public ulong GetNextSequenceId() => ++NextSequenceId;
    
    public Memory<byte> GetReceiveBuffer() => _receiveBuffer;
    public Memory<byte> GetSendBuffer() => _sendBuffer;

    public void AppendIncomingData(ReadOnlySpan<byte> data)
    {
        EnsureIncomingCapacity(data.Length);
        data.CopyTo(_incomingBuffer.AsSpan(_incomingLength));
        _incomingLength += data.Length;
    }

    public bool TryReadNextFrame(out byte[] frame)
    {
        frame = Array.Empty<byte>();

        if (_incomingLength < ProtocolHeaderSize)
        {
            return false;
        }

        var payloadLength = BinaryPrimitives.ReadUInt32BigEndian(_incomingBuffer.AsSpan(17, sizeof(uint)));
        if (payloadLength > ProtocolMaxPayloadSize)
        {
            throw new TransportError($"Payload too large: {payloadLength}");
        }

        var frameSize = ProtocolHeaderSize + checked((int)payloadLength) + ProtocolMacSize;
        if (_incomingLength < frameSize)
        {
            return false;
        }

        frame = new byte[frameSize];
        _incomingBuffer.AsSpan(0, frameSize).CopyTo(frame);

        var remaining = _incomingLength - frameSize;
        if (remaining > 0)
        {
            Buffer.BlockCopy(_incomingBuffer, frameSize, _incomingBuffer, 0, remaining);
        }

        _incomingLength = remaining;
        return true;
    }
    
    public virtual void UpdateActivity() => LastActivity = DateTime.UtcNow;

    private void EnsureIncomingCapacity(int additionalBytes)
    {
        var required = _incomingLength + additionalBytes;
        if (required <= _incomingBuffer.Length)
        {
            return;
        }

        var newSize = _incomingBuffer.Length;
        while (newSize < required)
        {
            newSize *= 2;
        }

        if (newSize > ProtocolMaxMessageSize * 2)
        {
            throw new TransportError("Incoming frame buffer exceeded safe maximum size");
        }

        var expanded = ArrayPool<byte>.Shared.Rent(newSize);
        _incomingBuffer.AsSpan(0, _incomingLength).CopyTo(expanded);
        ArrayPool<byte>.Shared.Return(_incomingBuffer);
        _incomingBuffer = expanded;
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        
        try { Socket?.Dispose(); } catch { }
        
        ArrayPool<byte>.Shared.Return(_receiveBuffer);
        ArrayPool<byte>.Shared.Return(_sendBuffer);
        Array.Clear(_incomingBuffer, 0, _incomingLength);
        ArrayPool<byte>.Shared.Return(_incomingBuffer);
        
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
