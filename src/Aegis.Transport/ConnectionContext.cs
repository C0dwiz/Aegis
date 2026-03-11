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
    private DateTime? _incompleteFrameLastUpdatedAt;
    private int _incompleteFrameDropCount;
    private long _inboundMaskOffset;
    private long _outboundMaskOffset;
    
    public Socket Socket { get; }
    public ulong ConnectionId { get; }
    public ulong NextSequenceId { get; private set; }
    public DateTime LastActivity { get; private set; }
    public bool HasPendingIncomingData => _incomingLength > 0;
    public int IncompleteFrameDropCount => _incompleteFrameDropCount;
    
    public ConnectionContext(Socket socket, ulong connectionId, int bufferSize = 8192)
    {
        Socket = socket ?? throw new ArgumentNullException(nameof(socket));
        ConnectionId = connectionId;
        _receiveBuffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        _sendBuffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        _incomingBuffer = ArrayPool<byte>.Shared.Rent(bufferSize * 2);
        _incomingLength = 0;
        _incompleteFrameLastUpdatedAt = null;
        _incompleteFrameDropCount = 0;
        LastActivity = DateTime.UtcNow;
    }
    
    public ulong GetNextSequenceId() => ++NextSequenceId;
    
    public Memory<byte> GetReceiveBuffer() => _receiveBuffer;
    public Memory<byte> GetSendBuffer() => _sendBuffer;

    public void AppendIncomingData(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return;
        }

        EnsureIncomingCapacity(data.Length);
        data.CopyTo(_incomingBuffer.AsSpan(_incomingLength));
        _incomingLength += data.Length;
        _incompleteFrameLastUpdatedAt = DateTime.UtcNow;
    }

    public bool TryReadNextFrame(out byte[] frame, out int frameLength)
    {
        frame = Array.Empty<byte>();
        frameLength = 0;

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

        frame = ArrayPool<byte>.Shared.Rent(frameSize);
        _incomingBuffer.AsSpan(0, frameSize).CopyTo(frame);
        frameLength = frameSize;

        var remaining = _incomingLength - frameSize;
        if (remaining > 0)
        {
            Buffer.BlockCopy(_incomingBuffer, frameSize, _incomingBuffer, 0, remaining);
            _incompleteFrameLastUpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _incompleteFrameLastUpdatedAt = null;
            _incompleteFrameDropCount = 0;
        }

        _incomingLength = remaining;
        return true;
    }

    public bool TryDropExpiredIncompleteFrame(TimeSpan timeout, out int droppedBytes)
    {
        droppedBytes = 0;
        if (_incomingLength == 0)
        {
            _incompleteFrameLastUpdatedAt = null;
            _incompleteFrameDropCount = 0;
            return false;
        }

        var lastUpdatedAt = _incompleteFrameLastUpdatedAt ?? DateTime.UtcNow;
        if (DateTime.UtcNow - lastUpdatedAt < timeout)
        {
            return false;
        }

        droppedBytes = _incomingLength;
        Array.Clear(_incomingBuffer, 0, _incomingLength);
        _incomingLength = 0;
        _incompleteFrameLastUpdatedAt = null;
        _incompleteFrameDropCount++;
        return true;
    }

    public int GetRemainingIncompleteFrameWaitMs(TimeSpan timeout)
    {
        if (_incomingLength == 0)
        {
            return int.MaxValue;
        }

        var lastUpdatedAt = _incompleteFrameLastUpdatedAt ?? DateTime.UtcNow;
        var elapsed = DateTime.UtcNow - lastUpdatedAt;
        var remaining = timeout - elapsed;
        if (remaining <= TimeSpan.Zero)
        {
            return 1;
        }

        var remainingMs = (int)Math.Ceiling(remaining.TotalMilliseconds);
        return Math.Max(1, remainingMs);
    }

    public void ApplyInboundMaskInPlace(Span<byte> buffer, ReadOnlySpan<byte> maskKey)
    {
        ApplyMask(buffer, maskKey, ref _inboundMaskOffset);
    }

    public void ApplyOutboundMaskInPlace(Span<byte> buffer, ReadOnlySpan<byte> maskKey)
    {
        ApplyMask(buffer, maskKey, ref _outboundMaskOffset);
    }

    private static void ApplyMask(Span<byte> buffer, ReadOnlySpan<byte> maskKey, ref long offset)
    {
        if (maskKey.Length == 0 || buffer.Length == 0)
        {
            return;
        }

        for (var i = 0; i < buffer.Length; i++)
        {
            var keyIndex = (int)((offset + i) % maskKey.Length);
            buffer[i] ^= maskKey[keyIndex];
        }

        offset += buffer.Length;
    }

    public bool TryReadNextFrame(out byte[] frame)
    {
        frame = Array.Empty<byte>();
        if (!TryReadNextFrame(out var pooledFrame, out var frameLength))
        {
            return false;
        }

        try
        {
            frame = new byte[frameLength];
            pooledFrame.AsSpan(0, frameLength).CopyTo(frame);
            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(pooledFrame);
        }
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
