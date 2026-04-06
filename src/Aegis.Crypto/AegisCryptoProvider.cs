using System.Security.Cryptography;
using Aegis.Common.Errors;
using Aegis.Protocol;
using Aegis.Common;

namespace Aegis.Crypto;

public class AegisCryptoProvider : ICryptoProvider, ISessionCryptoProvider
{
    private const int EncryptionKeySize = 32; // AES-256
    private const int NonceSize = 12; // AES-GCM nonce
    private const int TagSize = 16; // AES-GCM tag (integrity built-in)
    private const int BcryptWorkFactor = 12;
    private const int LegacyPasswordHashIterations = 210000;

    // ICryptoProvider implementation
    public async Task<string> HashPasswordAsync(string password)
    {
        var hash = BCrypt.Net.BCrypt.HashPassword(password, BcryptWorkFactor);
        return await Task.FromResult(hash);
    }

    public async Task<bool> VerifyPasswordAsync(string password, string hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            return false;
        }

        try
        {
            // New format: BCrypt
            if (hash.StartsWith("$2", StringComparison.Ordinal))
            {
                return await Task.FromResult(BCrypt.Net.BCrypt.Verify(password, hash));
            }

            // Legacy format fallback: PBKDF2(salt+hash)
            var hashBytes = Convert.FromBase64String(hash);
            if (hashBytes.Length < 16)
            {
                return false;
            }

            var salt = hashBytes.AsSpan(0, 16);
            var expectedHash = hashBytes.AsSpan(16);
            var computedHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, LegacyPasswordHashIterations, HashAlgorithmName.SHA256, 32);

            return await Task.FromResult(CryptographicOperations.FixedTimeEquals(expectedHash, computedHash));
        }
        catch
        {
            return await Task.FromResult(false);
        }
    }

    public async Task<string> HashAsync(string data)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(data));
        return await Task.FromResult(Convert.ToBase64String(hash));
    }

    public async Task<byte[]> GenerateSessionKeyAsync()
    {
        var key = new byte[EncryptionKeySize];
        RandomNumberGenerator.Fill(key);
        return await Task.FromResult(key);
    }

    // ISessionCryptoProvider implementation
    public async Task<byte[]> EncryptAsync(byte[] data, byte[] key)
    {
        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[data.Length + TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, data, ciphertext.AsSpan(0, data.Length), ciphertext.AsSpan(data.Length, TagSize));

        var result = new byte[nonce.Length + ciphertext.Length];
        nonce.AsSpan().CopyTo(result);
        ciphertext.AsSpan().CopyTo(result.AsSpan(nonce.Length));

        return await Task.FromResult(result);
    }

    public async Task<byte[]> DecryptAsync(byte[] encryptedData, byte[] key)
    {
        if (encryptedData.Length < NonceSize + TagSize)
            throw new CryptoError("Invalid encrypted data length");

        var nonce = encryptedData.AsSpan(0, NonceSize);
        var ciphertext = encryptedData.AsSpan(NonceSize);
        var actualCiphertext = ciphertext.Slice(0, ciphertext.Length - TagSize);
        var tag = ciphertext.Slice(ciphertext.Length - TagSize, TagSize);

        var plaintext = new byte[actualCiphertext.Length];
        using var aes = new AesGcm(key, TagSize);

        try
        {
            aes.Decrypt(nonce, actualCiphertext, tag, plaintext);
            return await Task.FromResult(plaintext);
        }
        catch (CryptographicException)
        {
            throw new CryptoError("Decryption failed - invalid authentication tag");
        }
    }

    public void DeriveKeys(ReadOnlySpan<byte> masterKey, Span<byte> encryptionKey)
    {
        if (encryptionKey.Length != EncryptionKeySize)
            throw new CryptoError($"Encryption key buffer must be {EncryptionKeySize} bytes");

        using var hkdf = new Rfc5869DeriveBytes(masterKey.ToArray(),
            Array.Empty<byte>(),
            "AegisKeyDerivation"u8.ToArray(),
            EncryptionKeySize);

        var derived = hkdf.GetBytes(EncryptionKeySize);
        derived.AsSpan(0, EncryptionKeySize).CopyTo(encryptionKey);
        CryptographicOperations.ZeroMemory(derived);
        hkdf.Dispose();
    }


    public int Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce, Span<byte> ciphertext,
        ReadOnlySpan<byte> aad = default)
    {
        if (ciphertext.Length < plaintext.Length + TagSize)
            throw new CryptoError($"Ciphertext buffer too small");

        var ciphertextBuffer = ciphertext.Slice(0, plaintext.Length);
        var tagBuffer = ciphertext.Slice(plaintext.Length, TagSize);

        using var aes = new AesGcm(key.ToArray(), TagSize);
        aes.Encrypt(nonce, plaintext, ciphertextBuffer, tagBuffer, aad);

        return plaintext.Length + TagSize;
    }

    public int Decrypt(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce, Span<byte> plaintext,
        ReadOnlySpan<byte> aad = default)
    {
        if (ciphertext.Length < TagSize || plaintext.Length < ciphertext.Length - TagSize)
            throw new CryptoError($"Invalid buffer sizes");

        var actualCiphertext = ciphertext.Slice(0, ciphertext.Length - TagSize);
        var tag = ciphertext.Slice(ciphertext.Length - TagSize, TagSize);

        using var aes = new AesGcm(key.ToArray(), TagSize);
        try
        {
            aes.Decrypt(nonce, actualCiphertext, tag, plaintext, aad);
            return ciphertext.Length - TagSize;
        }
        catch (CryptographicException)
        {
            throw new CryptoError("Decryption failed - invalid authentication tag");
        }
    }

    public Memory<byte> GenerateSessionKey()
    {
        var key = new byte[EncryptionKeySize];
        RandomNumberGenerator.Fill(key);
        return key;
    }

    public async Task<byte[]> EncryptMessageAsync(Message message, byte[] sessionKey)
    {
        var messageSize = ProtocolConstants.HeaderSize + message.PayloadLength;
        var messageBytes = new byte[messageSize];
        MessageEncoder.Encode(message, messageBytes);

        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        // Use header bytes as AAD so tampering with the header is detectable.
        var headerAad = messageBytes.AsSpan(0, ProtocolConstants.HeaderSize);
        var plainPayload = messageBytes.AsSpan(ProtocolConstants.HeaderSize, (int)message.PayloadLength);
        var ciphertext = new byte[plainPayload.Length + TagSize];
        var encryptedLength = Encrypt(plainPayload, sessionKey, nonce, ciphertext, headerAad);

        var result = new byte[nonce.Length + encryptedLength];
        Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
        Buffer.BlockCopy(ciphertext, 0, result, nonce.Length, encryptedLength);

        CryptographicOperations.ZeroMemory(ciphertext);
        CryptographicOperations.ZeroMemory(nonce);

        return await Task.FromResult(result);
    }
}

internal class Rfc5869DeriveBytes : IDisposable
{
    private readonly byte[] _prk;
    private readonly byte[] _info;
    private int _offset;
    private byte[] _t;
    private bool _disposed;

    public Rfc5869DeriveBytes(byte[] key, byte[] salt, byte[] info, int outputLength)
    {
        // Extract phase - HKDF-Extract
        using var hmac = new HMACSHA256(salt.Length == 0 ? new byte[32] : salt);
        _prk = hmac.ComputeHash(key);

        _info = info;
        _offset = 0;
        _t = new byte[32]; // HMAC-SHA256 output size
    }

    public byte[] GetBytes(int count)
    {
        var result = new byte[count];
        var resultOffset = 0;

        // Expand phase - HKDF-Expand with multiple rounds
        while (resultOffset < count)
        {
            if (_offset == 0)
            {
                // First round: HMAC(PRK, info || 0x01)
                using var hmac = new HMACSHA256(_prk);
                var input = new byte[_info.Length + 1];
                Buffer.BlockCopy(_info, 0, input, 0, _info.Length);
                input[_info.Length] = 0x01;
                _t = hmac.ComputeHash(input);
            }
            else
            {
                // Subsequent rounds: HMAC(PRK, T(previous) || info || counter)
                using var hmac = new HMACSHA256(_prk);
                var input = new byte[_t.Length + _info.Length + 1];
                Buffer.BlockCopy(_t, 0, input, 0, _t.Length);
                Buffer.BlockCopy(_info, 0, input, _t.Length, _info.Length);
                input[_t.Length + _info.Length] = (byte)(_offset + 1);
                _t = hmac.ComputeHash(input);
            }

            // Copy bytes to result
            var bytesToCopy = Math.Min(_t.Length, count - resultOffset);
            Buffer.BlockCopy(_t, 0, result, resultOffset, bytesToCopy);
            resultOffset += bytesToCopy;
            _offset++;
        }

        return result;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            CryptographicOperations.ZeroMemory(_prk);
            CryptographicOperations.ZeroMemory(_t);
            if (_info != null)
            {
                CryptographicOperations.ZeroMemory(_info);
            }
            _disposed = true;
        }
    }
}
