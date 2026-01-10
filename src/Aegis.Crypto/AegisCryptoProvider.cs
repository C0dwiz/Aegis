using System.Security.Cryptography;
using Aegis.Common.Errors;
using Aegis.Protocol;

namespace Aegis.Crypto;

public class AegisCryptoProvider : ICryptoProvider
{
    private const int EncryptionKeySize = 32; // AES-256
    private const int MacKeySize = 32; // HMAC-SHA256
    private const int NonceSize = 12; // AES-GCM nonce
    private const int TagSize = 16; // AES-GCM tag


    public void DeriveKeys(ReadOnlySpan<byte> masterKey, Span<byte> encryptionKey, Span<byte> macKey)
    {
        if (encryptionKey.Length != EncryptionKeySize || macKey.Length != MacKeySize)
            throw new CryptoError($"Invalid key buffer sizes");

        using var hkdf = new Rfc5869DeriveBytes(masterKey.ToArray(),
            Array.Empty<byte>(),
            "AegisKeyDerivation"u8.ToArray(),
            EncryptionKeySize + MacKeySize);

        var derived = hkdf.GetBytes(EncryptionKeySize + MacKeySize);
        derived.AsSpan(0, EncryptionKeySize).CopyTo(encryptionKey);
        derived.AsSpan(EncryptionKeySize, MacKeySize).CopyTo(macKey);

        CryptographicOperations.ZeroMemory(derived);
        hkdf.Dispose();
    }


    public int Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce, Span<byte> ciphertext)
    {
        if (ciphertext.Length < plaintext.Length + TagSize)
            throw new CryptoError($"Ciphertext buffer too small");

        var ciphertextBuffer = ciphertext.Slice(0, plaintext.Length);
        var tagBuffer = ciphertext.Slice(plaintext.Length, TagSize);
        
        using var aes = new AesGcm(key.ToArray(), TagSize);
        aes.Encrypt(nonce, plaintext, ciphertextBuffer, tagBuffer);

        return plaintext.Length + TagSize;
    }


    public int Decrypt(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce, Span<byte> plaintext)
    {
        if (ciphertext.Length < TagSize || plaintext.Length < ciphertext.Length - TagSize)
            throw new CryptoError($"Invalid buffer sizes");

        var actualCiphertext = ciphertext.Slice(0, ciphertext.Length - TagSize);
        var tag = ciphertext.Slice(ciphertext.Length - TagSize, TagSize);

        using var aes = new AesGcm(key.ToArray(), TagSize);
        try
        {
            aes.Decrypt(nonce, actualCiphertext, tag, plaintext);
            return ciphertext.Length - TagSize;
        }
        catch (CryptographicException)
        {
            throw new CryptoError("Decryption failed - invalid authentication tag");
        }
    }


    public void ComputeMac(ReadOnlySpan<byte> data, ReadOnlySpan<byte> key, Span<byte> mac)
    {
        if (mac.Length != ProtocolConstants.MacSize)
            throw new CryptoError($"MAC buffer must be {ProtocolConstants.MacSize} bytes");

        using var hmac = new HMACSHA256(key.ToArray());
        hmac.TryComputeHash(data, mac, out _);
    }

    public bool VerifyMac(ReadOnlySpan<byte> data, ReadOnlySpan<byte> key, ReadOnlySpan<byte> mac)
    {
        Span<byte> computed = stackalloc byte[ProtocolConstants.MacSize];
        ComputeMac(data, key, computed);
        return CryptographicOperations.FixedTimeEquals(computed, mac);
    }
}

internal class Rfc5869DeriveBytes : IDisposable
{
    private readonly byte[] _prk;
    private bool _disposed;
    public Rfc5869DeriveBytes(byte[] key, byte[] salt, byte[] info, int outputLength)
    {
        using var hmac = new HMACSHA256(salt);
        _prk = hmac.ComputeHash(key);

        // TODO: надо сделать правильную версию HKDF с несколькими раундами извлечения и расширения
        // Простое извлечение-затем-расширение
        using var expandHmac = new HMACSHA256(_prk);
        var result = expandHmac.ComputeHash(info);
        Array.Resize(ref result, outputLength);
    }
    public byte[] GetBytes(int count) => new byte[count];

    public void Dispose()
    {
        if (!_disposed)
        {
            CryptographicOperations.ZeroMemory(_prk);
            _disposed = true;
        }
    }
}
