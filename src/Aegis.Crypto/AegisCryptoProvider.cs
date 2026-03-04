using System.Security.Cryptography;
using Aegis.Common.Errors;
using Aegis.Protocol;
using Aegis.Common;

namespace Aegis.Crypto;

public class AegisCryptoProvider : ICryptoProvider, ISessionCryptoProvider
{
    private const int EncryptionKeySize = 32; // AES-256
    private const int MacKeySize = 32; // HMAC-SHA256
    private const int NonceSize = 12; // AES-GCM nonce
    private const int TagSize = 16; // AES-GCM tag

    // ICryptoProvider implementation
    public async Task<string> HashPasswordAsync(string password)
    {
        return await Task.Run(() =>
        {
            using var rng = RandomNumberGenerator.Create();
            var salt = new byte[16];
            rng.GetBytes(salt);
            
            // Use PBKDF2 with SHA256 - constructor with 4 parameters
            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 10000, HashAlgorithmName.SHA256);
            var hash = pbkdf2.GetBytes(32);
            
            // Combine salt and hash
            var result = new byte[salt.Length + hash.Length];
            salt.CopyTo(result, 0);
            hash.CopyTo(result, salt.Length);
            
            return Convert.ToBase64String(result);
        });
    }

    public async Task<bool> VerifyPasswordAsync(string password, string hash)
    {
        return await Task.Run(() =>
        {
            try
            {
                var hashBytes = Convert.FromBase64String(hash);
                if (hashBytes.Length < 16)
                    return false;
                
                var salt = hashBytes.AsSpan(0, 16);
                var expectedHash = hashBytes.AsSpan(16);
                
                // Use PBKDF2 with SHA256 - constructor with 4 parameters
                using var pbkdf2 = new Rfc2898DeriveBytes(password, salt.ToArray(), 10000, HashAlgorithmName.SHA256);
                var computedHash = pbkdf2.GetBytes(32);
                
                return CryptographicOperations.FixedTimeEquals(expectedHash, computedHash);
            }
            catch
            {
                return false;
            }
        });
    }

    public async Task<string> HashAsync(string data)
    {
        return await Task.Run(() =>
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(data));
            return Convert.ToBase64String(hash);
        });
    }

    public async Task<bool> VerifyMacAsync(byte[] data, byte[] key, byte[] mac)
    {
        return await Task.Run(() => VerifyMac(data, key, mac));
    }

    public async Task<byte[]> GenerateSessionKeyAsync()
    {
        return await Task.Run(() =>
        {
            var key = new byte[EncryptionKeySize];
            RandomNumberGenerator.Fill(key);
            return key;
        });
    }

    // ISessionCryptoProvider implementation
    public async Task<byte[]> EncryptAsync(byte[] data, byte[] key)
    {
        return await Task.Run(() =>
        {
            var nonce = new byte[NonceSize];
            RandomNumberGenerator.Fill(nonce);
            
            var ciphertext = new byte[data.Length + TagSize];
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, data, ciphertext.AsSpan(0, data.Length), ciphertext.AsSpan(data.Length, TagSize));
            
            var result = new byte[nonce.Length + ciphertext.Length];
            nonce.CopyTo(result);
            ciphertext.CopyTo(result.AsSpan(nonce.Length));
            
            return result;
        });
    }

    public async Task<byte[]> DecryptAsync(byte[] encryptedData, byte[] key)
    {
        return await Task.Run(() =>
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
                aes.Decrypt(nonce.ToArray(), actualCiphertext.ToArray(), tag.ToArray(), plaintext);
                return plaintext;
            }
            catch (CryptographicException)
            {
                throw new CryptoError("Decryption failed - invalid authentication tag");
            }
        });
    }

    public async Task<byte[]> GenerateMacAsync(byte[] data, byte[] key)
    {
        return await Task.Run(() =>
        {
            using var hmac = new HMACSHA256(key);
            return hmac.ComputeHash(data);
        });
    }


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

    public bool VerifyMac(byte[] data, byte[] key, byte[] mac)
    {
        Span<byte> computed = stackalloc byte[ProtocolConstants.MacSize];
        ComputeMac(data, key, computed);
        return CryptographicOperations.FixedTimeEquals(computed, mac);
    }

    public bool VerifyMac(ReadOnlySpan<byte> data, ReadOnlySpan<byte> key, ReadOnlySpan<byte> mac)
    {
        Span<byte> computed = stackalloc byte[ProtocolConstants.MacSize];
        ComputeMac(data, key, computed);
        return CryptographicOperations.FixedTimeEquals(computed, mac);
    }
    
    public Memory<byte> GenerateSessionKey()
    {
        var key = new byte[EncryptionKeySize];
        RandomNumberGenerator.Fill(key);
        return key;
    }
    
    public Memory<byte> GenerateMacKey()
    {
        var key = new byte[MacKeySize];
        RandomNumberGenerator.Fill(key);
        return key;
    }
    
    public async Task<byte[]> EncryptMessageAsync(Message message, byte[] sessionKey)
    {
        return await Task.Run(() =>
        {
            // Calculate message size
            var messageSize = ProtocolConstants.HeaderSize + message.PayloadLength + ProtocolConstants.MacSize;
            var messageBytes = new byte[messageSize];
            
            // Serialize message to bytes
            MessageEncoder.Encode(message, messageBytes);
            
            // Generate nonce
            var nonce = new byte[NonceSize];
            RandomNumberGenerator.Fill(nonce);
            
            // Encrypt the message (excluding MAC)
            var payloadSize = messageSize - ProtocolConstants.MacSize;
            var ciphertext = new byte[payloadSize + TagSize];
            var encryptedLength = Encrypt(new ReadOnlySpan<byte>(messageBytes, 0, (int)payloadSize), sessionKey, nonce, ciphertext);
            
            // Combine nonce + encrypted message
            var result = new byte[nonce.Length + encryptedLength];
            Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
            Buffer.BlockCopy(ciphertext, 0, result, nonce.Length, encryptedLength);
            
            // Compute MAC over the entire encrypted message
            var mac = new byte[ProtocolConstants.MacSize];
            ComputeMac(result, sessionKey, mac);
            
            // Final result: nonce + ciphertext + mac
            var finalResult = new byte[result.Length + mac.Length];
            Buffer.BlockCopy(result, 0, finalResult, 0, result.Length);
            Buffer.BlockCopy(mac, 0, finalResult, result.Length, mac.Length);
            
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(nonce);
            
            return finalResult;
        });
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
