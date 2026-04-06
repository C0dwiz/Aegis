using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Aegis.BotApi.Infrastructure.Auth;

public interface ICsrfProtectionService
{
    string IssueToken(string sessionToken);
    bool ValidateToken(string sessionToken, string csrfToken);
    void Revoke(string sessionToken);
}

public sealed class CsrfProtectionService : ICsrfProtectionService
{
    private static readonly TimeSpan TokenTtl = TimeSpan.FromHours(12);

    private readonly ConcurrentDictionary<string, CsrfRecord> _tokens = new();

    public string IssueToken(string sessionToken)
    {
        if (string.IsNullOrWhiteSpace(sessionToken))
        {
            throw new ArgumentException("Session token is required", nameof(sessionToken));
        }

        var csrfToken = GenerateToken();
        var record = new CsrfRecord(csrfToken, DateTime.UtcNow + TokenTtl);
        _tokens.AddOrUpdate(sessionToken, record, (_, _) => record);
        return csrfToken;
    }

    public bool ValidateToken(string sessionToken, string csrfToken)
    {
        if (string.IsNullOrWhiteSpace(sessionToken) || string.IsNullOrWhiteSpace(csrfToken))
        {
            return false;
        }

        if (!_tokens.TryGetValue(sessionToken, out var record))
        {
            return false;
        }

        if (record.ExpiresAtUtc < DateTime.UtcNow)
        {
            _tokens.TryRemove(sessionToken, out _);
            return false;
        }

        var left = System.Text.Encoding.UTF8.GetBytes(record.Token);
        var right = System.Text.Encoding.UTF8.GetBytes(csrfToken);
        return CryptographicOperations.FixedTimeEquals(left, right);
    }

    public void Revoke(string sessionToken)
    {
        if (!string.IsNullOrWhiteSpace(sessionToken))
        {
            _tokens.TryRemove(sessionToken, out _);
        }
    }

    private static string GenerateToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private sealed record CsrfRecord(string Token, DateTime ExpiresAtUtc);
}
