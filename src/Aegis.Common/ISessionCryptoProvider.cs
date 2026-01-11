namespace Aegis.Common;

public interface ISessionCryptoProvider
{
    Memory<byte> GenerateSessionKey();
    Memory<byte> GenerateMacKey();
    bool VerifyMac(ReadOnlySpan<byte> data, ReadOnlySpan<byte> key, ReadOnlySpan<byte> mac);
}
