namespace Aegis.Common;

public interface ICryptoProvider
{
    Task<string> HashPasswordAsync(string password);
    Task<bool> VerifyPasswordAsync(string password, string hash);
    Task<string> HashAsync(string data);
    Task<byte[]> GenerateSessionKeyAsync();
}

public interface IFullCryptoProvider : ISessionCryptoProvider, ICryptoProvider
{
    // Additional crypto methods that require full implementation
}
