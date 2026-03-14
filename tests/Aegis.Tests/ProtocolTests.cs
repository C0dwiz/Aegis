using Xunit;
using Aegis.Protocol;
using Aegis.DomainRules;
using Aegis.Common;
using Aegis.Common.Errors;

namespace Aegis.Tests;

public class ProtocolTests
{
    [Fact]
    public void EncodeDecode_ShouldPreserveData()
    {
        var original = new Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = 1,
            VersionMinor = 0,
            Flags = 0x01,
            Type = MessageType.Message,
            SequenceId = 12345,
            PayloadLength = 5,
            Payload = new byte[] { 1, 2, 3, 4, 5 },
            Mac = new byte[ProtocolConstants.MacSize]
        };
        
        var buffer = new byte[ProtocolConstants.HeaderSize + original.PayloadLength + ProtocolConstants.MacSize];
        MessageEncoder.Encode(original, buffer);
        var decoded = MessageEncoder.Decode(buffer);
        
        Assert.Equal(original.Magic, decoded.Magic);
        Assert.Equal(original.Type, decoded.Type);
        Assert.Equal(original.SequenceId, decoded.SequenceId);
        Assert.Equal(original.PayloadLength, decoded.PayloadLength);
        Assert.Equal(original.Payload, decoded.Payload);
    }

    [Fact]
    public void Decode_WithExtraBytes_ShouldThrowProtocolError()
    {
        var original = new Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = 1,
            VersionMinor = 0,
            Type = MessageType.Ping,
            SequenceId = 77,
            Payload = new byte[] { 1, 2, 3 },
            PayloadLength = 3,
            Mac = new byte[ProtocolConstants.MacSize]
        };

        var exact = new byte[Message.TotalSize(original)];
        MessageEncoder.Encode(original, exact);

        var withTail = new byte[exact.Length + 5];
        exact.CopyTo(withTail, 0);

        Assert.Throws<ProtocolError>(() => MessageEncoder.Decode(withTail));
    }

    [Fact]
    public void MessageDeduplicator_ShouldRejectReplayAndStaleSequence()
    {
        var deduplicator = new MessageDeduplicator();

        Assert.True(deduplicator.TryAcceptSequence(1, 1000, out _));
        Assert.False(deduplicator.TryAcceptSequence(1, 1000, out var duplicateReason));
        Assert.Contains("duplicate", duplicateReason);

        // Keep sequence in window (window size = 1024)
        Assert.True(deduplicator.TryAcceptSequence(1, 1500, out _));

        // Too old relative to highest sequence -> stale replay
        Assert.False(deduplicator.TryAcceptSequence(1, 100, out var staleReason));
        Assert.Contains("stale", staleReason);
    }

    [Fact]
    public void Decode_RandomGarbage_ShouldOnlyYieldProtocolErrorsOrValidMessages()
    {
        var random = new Random(1337);

        for (var i = 0; i < 300; i++)
        {
            var size = random.Next(0, 512);
            var data = new byte[size];
            random.NextBytes(data);

            try
            {
                var message = MessageEncoder.Decode(data);
                Assert.Equal(ProtocolConstants.Magic, message.Magic);
                Assert.True(message.PayloadLength <= ProtocolConstants.MaxPayloadSize);
            }
            catch (ProtocolError)
            {
                // Expected for malformed frames.
            }
        }
    }

    [Fact]
    public void Decode_MalformedHeaderLengths_ShouldThrowProtocolError()
    {
        var message = new Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = ProtocolConstants.VersionMajor,
            VersionMinor = ProtocolConstants.VersionMinor,
            Type = MessageType.Message,
            SequenceId = 321,
            Payload = new byte[] { 1, 2, 3, 4 },
            PayloadLength = 4,
            Mac = new byte[ProtocolConstants.MacSize]
        };

        var frame = new byte[Message.TotalSize(message)];
        MessageEncoder.Encode(message, frame);

        // Corrupt payload length field in header: set max uint to trigger bounds violation.
        frame[15] = 0xFF;
        frame[16] = 0xFF;
        frame[17] = 0xFF;
        frame[18] = 0xFF;

        Assert.Throws<ProtocolError>(() => MessageEncoder.Decode(frame));
    }

    [Fact]
    public void ProtocolSafetyFacade_ValidateFrameEnvelope_ShouldReturnErrorForInvalidSize()
    {
        var error = ProtocolSafetyFacade.ValidateFrameEnvelope(
            frameLength: 40,
            payloadLength: 100,
            headerSize: ProtocolConstants.HeaderSize,
            macSize: ProtocolConstants.MacSize,
            maxPayload: ProtocolConstants.MaxPayloadSize);

        Assert.NotNull(error);
        Assert.Contains("Invalid frame size", error);
    }

    [Fact]
    public void ProtocolSafetyFacade_IsRoutableInboundType_ShouldMatchKnownAndUnknownTypes()
    {
        Assert.True(ProtocolSafetyFacade.IsRoutableInboundType((ushort)MessageType.Handshake));
        Assert.True(ProtocolSafetyFacade.IsRoutableInboundType((ushort)MessageType.PrivateChatMessage));
        Assert.False(ProtocolSafetyFacade.IsRoutableInboundType((ushort)MessageType.Error));
        Assert.False(ProtocolSafetyFacade.IsRoutableInboundType(65000));
    }
}
