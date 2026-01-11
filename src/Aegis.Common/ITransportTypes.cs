namespace Aegis.Common;

// Forward declarations for transport types
public interface IConnectionContext
{
    ulong ConnectionId { get; }
    DateTime LastActivity { get; }
    void UpdateActivity();
}

public interface IMessage
{
    uint Magic { get; set; }
    byte VersionMajor { get; set; }
    byte VersionMinor { get; set; }
    byte Flags { get; set; }
    ushort Type { get; set; }
    ulong SequenceId { get; set; }
    uint PayloadLength { get; set; }
    Memory<byte> Payload { get; set; }
    Memory<byte> Mac { get; set; }
}

public interface ITcpServer
{
    Task SendAsync(IConnectionContext context, byte[] data);
}
