using Aegis.Common;

namespace Aegis.Crypto;

/// <summary>
/// Adapter that implements Aegis.Common.ICryptoProvider using Aegis.Crypto.ICryptoProvider
/// </summary>
public class CommonCryptoProviderAdapter : Aegis.Common.ICryptoProvider
{
    private readonly AegisCryptoProvider _cryptoProvider;

    public CommonCryptoProviderAdapter(AegisCryptoProvider cryptoProvider)
    {
        _cryptoProvider = cryptoProvider;
    }

    public Task<string> HashPasswordAsync(string password)
    {
        return _cryptoProvider.HashPasswordAsync(password);
    }

    public Task<bool> VerifyPasswordAsync(string password, string hash)
    {
        return _cryptoProvider.VerifyPasswordAsync(password, hash);
    }

    public Task<string> HashAsync(string data)
    {
        return _cryptoProvider.HashAsync(data);
    }

    public Task<byte[]> GenerateSessionKeyAsync()
    {
        return _cryptoProvider.GenerateSessionKeyAsync();
    }
}
