namespace Aegis.Protocol;

public static class ProtocolConstants
{
    public const uint Magic = 0xAE6C5D7; // AEGIS magic
    public const byte VersionMajor = 1;
    public const byte VersionMinor = 0;
    public const int HeaderSize = sizeof(uint) + sizeof(byte) * 3 + sizeof(ushort) + sizeof(ulong) + sizeof(uint);
    public const int MacSize = 32; // SHA256 HMAC
    public const int MaxMessageSize = 1024 * 1024; // 1MB
    public const int MaxPayloadSize = MaxMessageSize - HeaderSize - MacSize;
}
