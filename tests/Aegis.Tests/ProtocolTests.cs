using Xunit;
using Aegis.Protocol;

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
}
