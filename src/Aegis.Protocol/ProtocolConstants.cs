namespace Aegis.Protocol;

public static class ProtocolConstants
{
    public const uint Magic = 0xAE6C5D7; // AEGIS magic
    public const byte VersionMajor = 1;
    public const byte VersionMinor = 0;
    public const int HeaderSize = sizeof(uint) + sizeof(byte) * 3 + sizeof(ushort) + sizeof(ulong) + sizeof(uint);
    // AES-GCM tag (16 bytes) is embedded in the ciphertext — no separate frame-level HMAC.
    public const int MacSize = 0;
    public const int MaxMessageSize = 1024 * 1024; // 1MB
    public const int MaxPayloadSize = MaxMessageSize - HeaderSize;
    // Compress payload with Brotli when raw bytes exceed this threshold.
    public const int CompressionThreshold = 512;
}
