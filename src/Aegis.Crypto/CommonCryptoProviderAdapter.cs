using Aegis.Common;

namespace Aegis.Crypto;

/// <summary>
/// Adapter that implements Aegis.Common.ICryptoProvider using Aegis.Crypto.ICryptoProvider
/// </summary>
public class CommonCryptoProviderAdapter : Aegis.Common.ICryptoProvider
{
    private readonly ICryptoProvider _cryptoProvider;

    public CommonCryptoProviderAdapter(ICryptoProvider cryptoProvider)
    {
        _cryptoProvider = cryptoProvider;
    }

    public Task<string> HashPasswordAsync(string password)
    {
        // For now, use a simple hash - in a real implementation, this should use proper password hashing
        return Task.FromResult(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(password)));
    }

    public Task<bool> VerifyPasswordAsync(string password, string hash)
    {
        var computedHash = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(password));
        return Task.FromResult(computedHash == hash);
    }

    public Task<string> HashAsync(string data)
    {
        // Simple hash implementation - in production, use proper cryptographic hash
        return Task.FromResult(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(data)));
    }

    public Task<bool> VerifyMacAsync(byte[] data, byte[] key, byte[] mac)
    {
        // Simple MAC verification - in production, use proper HMAC
        var computedMac = ComputeMac(data, key);
        return Task.FromResult(computedMac.SequenceEqual(mac));
    }

    public Task<byte[]> GenerateSessionKeyAsync()
    {
        return Task.FromResult(_cryptoProvider.GenerateSessionKey().ToArray());
    }

    private byte[] ComputeMac(byte[] data, byte[] key)
    {
        // Simple MAC computation - in production, use proper HMAC
        using var hmac = System.Security.Cryptography.HMACSHA256.Create();
        hmac.Key = key;
        return hmac.ComputeHash(data);
    }
}
