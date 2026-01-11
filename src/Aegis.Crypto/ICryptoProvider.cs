using Aegis.Common.Errors;
using Aegis.Protocol;

namespace Aegis.Crypto;

public interface ICryptoProvider
{
    void DeriveKeys(ReadOnlySpan<byte> masterKey, Span<byte> encryptionKey, Span<byte> macKey);
    int Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, Span<byte> ciphertext);
    int Decrypt(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, Span<byte> plaintext);
    void ComputeMac(ReadOnlySpan<byte> data, ReadOnlySpan<byte> key, Span<byte> mac);
    bool VerifyMac(ReadOnlySpan<byte> data, ReadOnlySpan<byte> key, ReadOnlySpan<byte> mac);
    
    // Additional methods for message encryption and session management
    Memory<byte> GenerateSessionKey();
    Memory<byte> GenerateMacKey();
    Task<byte[]> EncryptMessageAsync(Message message, byte[] sessionKey);
}
