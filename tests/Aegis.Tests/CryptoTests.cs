using Xunit;
using System.Security.Cryptography;
using Aegis.Crypto;
using Aegis.Common.Errors;
using Aegis.Protocol;

namespace Aegis.Tests;

public class CryptoTests
{
    private readonly AegisCryptoProvider _crypto = new AegisCryptoProvider();

    [Fact]
    public void DeriveKeys_ShouldGenerateValidKeys()
    {
        // Arrange
        var masterKey = new byte[32];
        RandomNumberGenerator.Fill(masterKey);
        var encryptionKey = new byte[32];

        // Act
        _crypto.DeriveKeys(masterKey, encryptionKey);

        // Assert
        Assert.NotEmpty(encryptionKey);
        Assert.Equal(32, encryptionKey.Length);
        Assert.NotEqual(masterKey, encryptionKey);
    }

    [Fact]
    public void DeriveKeys_InvalidKeySizes_ShouldThrowError()
    {
        // Arrange
        var masterKey = new byte[32];
        var smallKey = new byte[16];

        // Act & Assert
        Assert.Throws<CryptoError>(() => _crypto.DeriveKeys(masterKey, smallKey));
    }

    [Fact]
    public void EncryptDecrypt_ShouldPreserveData()
    {
        // Arrange
        var plaintext = "Hello, World!"u8.ToArray();
        var key = new byte[32];
        var nonce = new byte[12];
        RandomNumberGenerator.Fill(key);
        RandomNumberGenerator.Fill(nonce);
        var ciphertext = new byte[plaintext.Length + 16];

        // Act
        var encryptedLength = _crypto.Encrypt(plaintext, key, nonce, ciphertext);
        var decrypted = new byte[plaintext.Length];
        var decryptedLength = _crypto.Decrypt(ciphertext, key, nonce, decrypted);

        // Assert
        Assert.Equal(plaintext.Length + 16, encryptedLength);
        Assert.Equal(plaintext.Length, decryptedLength);
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_SmallBuffer_ShouldThrowError()
    {
        // Arrange
        var plaintext = new byte[100];
        var key = new byte[32];
        var nonce = new byte[12];
        var smallBuffer = new byte[50];

        // Act & Assert
        Assert.Throws<CryptoError>(() => _crypto.Encrypt(plaintext, key, nonce, smallBuffer));
    }

    [Fact]
    public void Decrypt_InvalidTag_ShouldThrowError()
    {
        // Arrange
        var plaintext = "Hello, World!"u8.ToArray();
        var key = new byte[32];
        var nonce = new byte[12];
        RandomNumberGenerator.Fill(key);
        RandomNumberGenerator.Fill(nonce);
        var ciphertext = new byte[plaintext.Length + 16];
        _crypto.Encrypt(plaintext, key, nonce, ciphertext);

        // Corrupt the authentication tag
        ciphertext[ciphertext.Length - 1] ^= 0xFF;

        var decrypted = new byte[plaintext.Length];

        // Act & Assert
        Assert.Throws<CryptoError>(() => _crypto.Decrypt(ciphertext, key, nonce, decrypted));
    }

    [Fact]
    public void EncryptDecrypt_WithAad_ShouldVerifyAad()
    {
        // Arrange: AES-GCM with AAD should fail if AAD is tampered.
        var plaintext = "Hello, AAD!"u8.ToArray();
        var key = new byte[32];
        var nonce = new byte[12];
        var aad = "header-bytes"u8.ToArray();
        RandomNumberGenerator.Fill(key);
        RandomNumberGenerator.Fill(nonce);
        var ciphertext = new byte[plaintext.Length + 16];

        _crypto.Encrypt(plaintext, key, nonce, ciphertext, aad);

        // Correct AAD should decrypt successfully.
        var decrypted = new byte[plaintext.Length];
        _crypto.Decrypt(ciphertext, key, nonce, decrypted, aad);
        Assert.Equal(plaintext, decrypted);

        // Wrong AAD must throw.
        var wrongAad = "tampered-header"u8.ToArray();
        Assert.Throws<CryptoError>(() => _crypto.Decrypt(ciphertext, key, nonce, decrypted, wrongAad));
    }
}
