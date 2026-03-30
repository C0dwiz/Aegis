using System.Buffers.Binary;
using Aegis.DomainRules;
using Aegis.Common.Errors;

namespace Aegis.Protocol;

public static class MessageEncoder
{
    public static int Encode(Message message, Span<byte> buffer)
    {
        if (buffer.Length < ProtocolConstants.HeaderSize + message.PayloadLength)
            throw new ProtocolError($"Buffer too small: {buffer.Length}");

        int offset = 0;

        // Write header
        BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(offset), message.Magic);
        offset += sizeof(uint);

        buffer[offset++] = message.VersionMajor;
        buffer[offset++] = message.VersionMinor;
        buffer[offset++] = message.Flags;

        BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(offset), (ushort)message.Type);
        offset += sizeof(ushort);

        BinaryPrimitives.WriteUInt64BigEndian(buffer.Slice(offset), message.SequenceId);
        offset += sizeof(ulong);

        BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(offset), message.PayloadLength);
        offset += sizeof(uint);

        // Write payload
        if (message.PayloadLength > 0)
        {
            message.Payload.AsSpan(0, (int)message.PayloadLength).CopyTo(buffer.Slice(offset));
            offset += (int)message.PayloadLength;
        }

        return offset;
    }

    public static Message Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < ProtocolConstants.HeaderSize)
            throw new ProtocolError($"Message too short: {data.Length}");

        int offset = 0;
        var message = new Message();

        // Read header
        message.Magic = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset));
        offset += sizeof(uint);

        if (message.Magic != ProtocolConstants.Magic)
            throw new ProtocolError($"Invalid magic: 0x{message.Magic:X}");

        message.VersionMajor = data[offset++];
        message.VersionMinor = data[offset++];
        message.Flags = data[offset++];

        if (message.VersionMajor != ProtocolConstants.VersionMajor)
            throw new ProtocolError($"Unsupported protocol version: {message.VersionMajor}.{message.VersionMinor}");

        message.Type = (MessageType)BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset));
        offset += sizeof(ushort);

        message.SequenceId = BinaryPrimitives.ReadUInt64BigEndian(data.Slice(offset));
        offset += sizeof(ulong);

        message.PayloadLength = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset));
        offset += sizeof(uint);

        var frameError = ProtocolSafetyFacade.ValidateFrameEnvelope(
            data.Length,
            message.PayloadLength,
            ProtocolConstants.HeaderSize,
            0,
            ProtocolConstants.MaxPayloadSize);

        if (frameError != null)
            throw new ProtocolError(frameError);

        var expectedSize = ProtocolConstants.HeaderSize + checked((int)message.PayloadLength);

        if (data.Length - offset < message.PayloadLength)
            throw new ProtocolError("Incomplete message");

        // Read payload
        if (message.PayloadLength > 0)
        {
            message.Payload = new byte[message.PayloadLength];
            data.Slice(offset, (int)message.PayloadLength).CopyTo(message.Payload);
            offset += (int)message.PayloadLength;
        }

        return message;
    }
}
