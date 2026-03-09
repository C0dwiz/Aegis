using Xunit;
using Aegis.Protocol;
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
}
