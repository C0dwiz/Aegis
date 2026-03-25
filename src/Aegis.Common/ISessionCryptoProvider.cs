namespace Aegis.Common;

public interface ISessionCryptoProvider
{
    Task<byte[]> EncryptAsync(byte[] data, byte[] key);
    Task<byte[]> DecryptAsync(byte[] encryptedData, byte[] key);
    Memory<byte> GenerateSessionKey();
}
