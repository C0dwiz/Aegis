namespace Aegis.Protocol;

public enum MessageType : ushort
{
    Unknown = 0,
    Auth = 1,
    Ping = 2,
    Message = 3,
    Ack = 4,
    Error = 5,
    Handshake = 6
}
