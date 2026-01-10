using System.Buffers.Binary;

namespace Aegis.Protocol;

public class Message
{
    public uint Magic { get; set; }
    public byte VersionMajor { get; set; }
    public byte VersionMinor { get; set; }
    public byte Flags { get; set; }
    public MessageType Type { get; set; }
    public ulong SequenceId { get; set; }
    public uint PayloadLength { get; set; }
    public byte[] Payload { get; set; } = Array.Empty<byte>();
    public byte[] Mac { get; set; } = new byte[ProtocolConstants.MacSize];

    public static int TotalSize(Message message) => 
        ProtocolConstants.HeaderSize + (int)message.PayloadLength + ProtocolConstants.MacSize;
}
