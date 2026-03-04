namespace Aegis.Common;

public interface ISessionCryptoProvider
{
    Task<byte[]> EncryptAsync(byte[] data, byte[] key);
    Task<byte[]> DecryptAsync(byte[] encryptedData, byte[] key);
    Task<byte[]> GenerateMacAsync(byte[] data, byte[] key);
    bool VerifyMac(byte[] data, byte[] key, byte[] mac);
    Memory<byte> GenerateSessionKey();
    Memory<byte> GenerateMacKey();
}
