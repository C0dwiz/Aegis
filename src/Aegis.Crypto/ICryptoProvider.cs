using Aegis.Common.Errors;
using Aegis.Protocol;

namespace Aegis.Crypto;

public interface ICryptoProvider
{
    // Derives only the session encryption key via HKDF.
    void DeriveKeys(ReadOnlySpan<byte> masterKey, Span<byte> encryptionKey);

    // AES-GCM encrypt. Pass header bytes as aad to bind them to the ciphertext.
    int Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce, Span<byte> ciphertext,
        ReadOnlySpan<byte> aad = default);

    // AES-GCM decrypt. aad must match what was used during encryption.
    int Decrypt(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce, Span<byte> plaintext,
        ReadOnlySpan<byte> aad = default);

    Memory<byte> GenerateSessionKey();
    Task<byte[]> EncryptMessageAsync(Message message, byte[] sessionKey);
}
