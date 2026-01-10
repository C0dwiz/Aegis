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
        var macKey = new byte[32];
        
        // Act
        _crypto.DeriveKeys(masterKey, encryptionKey, macKey);
        
        // Assert
        Assert.NotEmpty(encryptionKey);
        Assert.NotEmpty(macKey);
        Assert.Equal(32, encryptionKey.Length);
        Assert.Equal(32, macKey.Length);
        Assert.NotEqual(masterKey, encryptionKey);
        Assert.NotEqual(masterKey, macKey);
    }
    
    [Fact]
    public void DeriveKeys_InvalidKeySizes_ShouldThrowError()
    {
        // Arrange
        var masterKey = new byte[32];
        var smallKey = new byte[16];
        var macKey = new byte[32];
        
        // Act & Assert
        Assert.Throws<CryptoError>(() => _crypto.DeriveKeys(masterKey, smallKey, macKey));
        Assert.Throws<CryptoError>(() => _crypto.DeriveKeys(masterKey, macKey, smallKey));
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
    public void ComputeMac_VerifyMac_ShouldMatch()
    {
        // Arrange
        var data = "Test data for MAC"u8.ToArray();
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        var mac1 = new byte[32];
        var mac2 = new byte[32];
        
        // Act
        _crypto.ComputeMac(data, key, mac1);
        var isValid = _crypto.VerifyMac(data, key, mac2);
        
        // Assert
        Assert.False(isValid); // Different MACs should not match
        
        _crypto.ComputeMac(data, key, mac2);
        isValid = _crypto.VerifyMac(data, key, mac2);
        Assert.True(isValid); // Same MAC should match
    }
    
    [Fact]
    public void ComputeMac_InvalidBuffer_ShouldThrowError()
    {
        // Arrange
        var data = new byte[10];
        var key = new byte[32];
        var smallMac = new byte[16];
        
        // Act & Assert
        Assert.Throws<CryptoError>(() => _crypto.ComputeMac(data, key, smallMac));
    }
    
    [Fact]
    public void VerifyMac_TamperedData_ShouldFail()
    {
        // Arrange
        var data = "Original data"u8.ToArray();
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        var mac = new byte[32];
        _crypto.ComputeMac(data, key, mac);
        
        // Tamper with data
        var tamperedData = (byte[])data.Clone();
        tamperedData[0] ^= 0xFF;
        
        // Act
        var isValid = _crypto.VerifyMac(tamperedData, key, mac);
        
        // Assert
        Assert.False(isValid);
    }
}
