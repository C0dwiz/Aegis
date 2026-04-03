using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Aegis.Data.Entities;
using Aegis.Data.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Data.Services;

public enum EmailChallengePurpose
{
    VerifyEmail = 0,
    ResetPassword = 1,
    LoginCode = 2
}

public interface IEmailChallengeService
{
    Task<string> IssueCodeAsync(EmailChallengePurpose purpose, string email, TimeSpan ttl);
    Task<bool> ValidateCodeAsync(EmailChallengePurpose purpose, string email, string code, bool consume = true);
}

public sealed class EmailChallengeService : IEmailChallengeService
{
    private readonly IDistributedCache _cache;

    public EmailChallengeService(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task<string> IssueCodeAsync(EmailChallengePurpose purpose, string email, TimeSpan ttl)
    {
        var normalizedEmail = NormalizeEmail(email);
        var code = GenerateNumericCode();
        var key = BuildKey(purpose, normalizedEmail);

        await _cache.SetStringAsync(
            key,
            code,
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl
            });

        return code;
    }

    public async Task<bool> ValidateCodeAsync(EmailChallengePurpose purpose, string email, string code, bool consume = true)
    {
        var normalizedEmail = NormalizeEmail(email);
        var key = BuildKey(purpose, normalizedEmail);
        var expected = await _cache.GetStringAsync(key);
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var left = Encoding.UTF8.GetBytes(expected);
        var right = Encoding.UTF8.GetBytes(code.Trim());
        var ok = left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);

        if (ok && consume)
        {
            await _cache.RemoveAsync(key);
        }

        return ok;
    }

    private static string BuildKey(EmailChallengePurpose purpose, string email)
    {
        return $"auth:challenge:{purpose}:{email}";
    }

    private static string NormalizeEmail(string email)
    {
        return UserRegistrationService.NormalizeEmail(email);
    }

    private static string GenerateNumericCode()
    {
        Span<byte> bytes = stackalloc byte[4];
        RandomNumberGenerator.Fill(bytes);
        var value = BitConverter.ToUInt32(bytes) % 1_000_000;
        return value.ToString("D6", CultureInfo.InvariantCulture);
    }
}

public sealed record TotpSetupResult(string Secret, string OtpauthUri, string RecoveryPhrase);

public interface IUserTwoFactorService
{
    Task<TotpSetupResult> BeginSetupAsync(ulong userId, string issuer);
    Task<bool> EnableAsync(ulong userId, string code);
    Task<bool> ValidateAsync(User user, string? code, string? recoveryPhrase);
    Task<bool> DisableAsync(ulong userId, string code, string? recoveryPhrase);
    Task<int> ReencryptLegacySecretsAsync(CancellationToken cancellationToken = default);
}

public sealed class UserTwoFactorService : IUserTwoFactorService
{
    private const int TotpDigits = 6;
    private const int TotpPeriodSeconds = 30;
    private const string EncryptedTotpPrefix = "enc1:";

    private readonly IUserRepository _userRepository;
    private readonly Aegis.Common.ICryptoProvider _cryptoProvider;
    private readonly AegisDbContext _dbContext;
    private readonly byte[]? _totpEncryptionKey;

    public UserTwoFactorService(
        IUserRepository userRepository,
        Aegis.Common.ICryptoProvider cryptoProvider,
        AegisDbContext dbContext,
        IConfiguration configuration)
    {
        _userRepository = userRepository;
        _cryptoProvider = cryptoProvider;
        _dbContext = dbContext;
        _totpEncryptionKey = ParseTotpEncryptionKey(configuration["Security:TotpEncryptionKey"]);
    }

    public async Task<int> ReencryptLegacySecretsAsync(CancellationToken cancellationToken = default)
    {
        if (_totpEncryptionKey == null)
        {
            return 0;
        }

        var users = await _dbContext.Users
            .Where(u => u.TotpSecret != null && !u.TotpSecret.StartsWith(EncryptedTotpPrefix))
            .ToListAsync(cancellationToken);

        if (users.Count == 0)
        {
            return 0;
        }

        foreach (var user in users)
        {
            user.TotpSecret = ProtectTotpSecret(user.TotpSecret!);
            user.UpdatedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return users.Count;
    }

    public async Task<TotpSetupResult> BeginSetupAsync(ulong userId, string issuer)
    {
        var user = await _userRepository.GetByIdAsync(userId)
            ?? throw new InvalidOperationException("User not found");

        var secretBytes = new byte[20];
        RandomNumberGenerator.Fill(secretBytes);
        var secret = Base32.Encode(secretBytes);
        var recoveryPhrase = GenerateRecoveryPhrase();
        var recoveryHash = await HashRecoveryPhraseAsync(recoveryPhrase);

        user.TotpSecret = ProtectTotpSecret(secret);
        user.RecoveryPhraseHash = recoveryHash;
        user.TwoFactorEnabled = false;
        user.UpdatedAt = DateTime.UtcNow;
        await _userRepository.UpdateAsync(user);

        var issuerEscaped = Uri.EscapeDataString(string.IsNullOrWhiteSpace(issuer) ? "Aegis" : issuer.Trim());
        var accountEscaped = Uri.EscapeDataString(user.Username);
        var otpauthUri = $"otpauth://totp/{issuerEscaped}:{accountEscaped}?secret={secret}&issuer={issuerEscaped}&digits={TotpDigits}&period={TotpPeriodSeconds}";

        return new TotpSetupResult(secret, otpauthUri, recoveryPhrase);
    }

    public async Task<bool> EnableAsync(ulong userId, string code)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null || string.IsNullOrWhiteSpace(user.TotpSecret))
        {
            return false;
        }

        if (!TryGetPlainTotpSecret(user.TotpSecret, out var secret) || !VerifyTotpCode(secret, code))
        {
            return false;
        }

        user.TwoFactorEnabled = true;
        user.UpdatedAt = DateTime.UtcNow;
        await _userRepository.UpdateAsync(user);
        return true;
    }

    public async Task<bool> ValidateAsync(User user, string? code, string? recoveryPhrase)
    {
        if (!user.TwoFactorEnabled)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(user.TotpSecret)
            && TryGetPlainTotpSecret(user.TotpSecret, out var secret)
            && !string.IsNullOrWhiteSpace(code)
            && VerifyTotpCode(secret, code))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(recoveryPhrase) || string.IsNullOrWhiteSpace(user.RecoveryPhraseHash))
        {
            return false;
        }

        var normalized = NormalizeRecoveryPhrase(recoveryPhrase);
        var hash = await _cryptoProvider.HashAsync(normalized);
        var left = Encoding.UTF8.GetBytes(hash);
        var right = Encoding.UTF8.GetBytes(user.RecoveryPhraseHash);
        var ok = left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
        if (!ok)
        {
            return false;
        }

        // Recovery phrase is one-time: disable 2FA and force re-enrollment.
        user.TwoFactorEnabled = false;
        user.TotpSecret = null;
        user.RecoveryPhraseHash = null;
        user.UpdatedAt = DateTime.UtcNow;
        await _userRepository.UpdateAsync(user);
        return true;
    }

    public async Task<bool> DisableAsync(ulong userId, string code, string? recoveryPhrase)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null || !user.TwoFactorEnabled)
        {
            return false;
        }

        var byCode = !string.IsNullOrWhiteSpace(user.TotpSecret)
            && TryGetPlainTotpSecret(user.TotpSecret, out var secret)
            && VerifyTotpCode(secret, code);
        var byRecovery = false;

        if (!byCode && !string.IsNullOrWhiteSpace(recoveryPhrase) && !string.IsNullOrWhiteSpace(user.RecoveryPhraseHash))
        {
            var normalized = NormalizeRecoveryPhrase(recoveryPhrase);
            var hash = await _cryptoProvider.HashAsync(normalized);
            var left = Encoding.UTF8.GetBytes(hash);
            var right = Encoding.UTF8.GetBytes(user.RecoveryPhraseHash);
            byRecovery = left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
        }

        if (!byCode && !byRecovery)
        {
            return false;
        }

        user.TwoFactorEnabled = false;
        user.TotpSecret = null;
        user.RecoveryPhraseHash = null;
        user.UpdatedAt = DateTime.UtcNow;
        await _userRepository.UpdateAsync(user);
        return true;
    }

    private async Task<string> HashRecoveryPhraseAsync(string recoveryPhrase)
    {
        var normalized = NormalizeRecoveryPhrase(recoveryPhrase);
        return await _cryptoProvider.HashAsync(normalized);
    }

    private string ProtectTotpSecret(string secret)
    {
        if (_totpEncryptionKey == null)
        {
            return secret;
        }

        var plaintext = Encoding.UTF8.GetBytes(secret);
        var nonce = new byte[12];
        var tag = new byte[16];
        var ciphertext = new byte[plaintext.Length];
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(_totpEncryptionKey, tag.Length);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var payload = new byte[nonce.Length + ciphertext.Length + tag.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(ciphertext, 0, payload, nonce.Length, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length + ciphertext.Length, tag.Length);

        return EncryptedTotpPrefix + Convert.ToBase64String(payload);
    }

    private bool TryGetPlainTotpSecret(string storedSecret, out string secret)
    {
        secret = string.Empty;
        if (string.IsNullOrWhiteSpace(storedSecret))
        {
            return false;
        }

        if (!storedSecret.StartsWith(EncryptedTotpPrefix, StringComparison.Ordinal))
        {
            secret = storedSecret;
            return true;
        }

        if (_totpEncryptionKey == null)
        {
            return false;
        }

        var encoded = storedSecret[EncryptedTotpPrefix.Length..];
        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(encoded);
        }
        catch
        {
            return false;
        }

        if (payload.Length < 12 + 16)
        {
            return false;
        }

        var nonce = payload.AsSpan(0, 12);
        var ciphertext = payload.AsSpan(12, payload.Length - 12 - 16);
        var tag = payload.AsSpan(payload.Length - 16, 16);
        var plaintext = new byte[ciphertext.Length];

        try
        {
            using var aes = new AesGcm(_totpEncryptionKey, tag.Length);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            secret = Encoding.UTF8.GetString(plaintext);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static byte[]? ParseTotpEncryptionKey(string? configuredValue)
    {
        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(configuredValue);
            return bytes.Length == 32 ? bytes : null;
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeRecoveryPhrase(string phrase)
    {
        return string.Join(' ', (phrase ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool VerifyTotpCode(string secretBase32, string code)
    {
        var normalizedCode = new string((code ?? string.Empty).Where(char.IsDigit).ToArray());
        if (normalizedCode.Length != TotpDigits)
        {
            return false;
        }

        var secret = Base32.Decode(secretBase32);
        var unix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var counter = unix / TotpPeriodSeconds;

        // Accept one step before/after to tolerate client clock skew.
        for (var offset = -1; offset <= 1; offset++)
        {
            var expected = ComputeTotp(secret, counter + offset, TotpDigits);
            var left = Encoding.UTF8.GetBytes(expected);
            var right = Encoding.UTF8.GetBytes(normalizedCode);
            if (left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right))
            {
                return true;
            }
        }

        return false;
    }

    private static string ComputeTotp(byte[] secret, long counter, int digits)
    {
        Span<byte> counterBytes = stackalloc byte[8];
        var temp = counter;
        for (var i = 7; i >= 0; i--)
        {
            counterBytes[i] = (byte)(temp & 0xff);
            temp >>= 8;
        }

        using var hmac = new HMACSHA1(secret);
        var hash = hmac.ComputeHash(counterBytes.ToArray());
        var offset = hash[^1] & 0x0f;

        var binaryCode = ((hash[offset] & 0x7f) << 24)
                         | ((hash[offset + 1] & 0xff) << 16)
                         | ((hash[offset + 2] & 0xff) << 8)
                         | (hash[offset + 3] & 0xff);

        var otp = binaryCode % (int)Math.Pow(10, digits);
        return otp.ToString($"D{digits}", CultureInfo.InvariantCulture);
    }

    private static string GenerateRecoveryPhrase()
    {
        var words = RecoveryWords;
        var selected = new string[20];
        Span<byte> bytes = stackalloc byte[4];

        for (var i = 0; i < selected.Length; i++)
        {
            RandomNumberGenerator.Fill(bytes);
            var index = (int)(BitConverter.ToUInt32(bytes) % (uint)words.Length);
            selected[i] = words[index];
        }

        return string.Join(' ', selected);
    }

    private static readonly string[] RecoveryWords =
    {
        "amber", "anchor", "apple", "arrow", "atlas", "autumn", "badge", "balance", "bamboo", "beacon",
        "berry", "blossom", "breeze", "bronze", "canyon", "carbon", "cedar", "circle", "cobalt", "comet",
        "coral", "cosmos", "crystal", "dawn", "delta", "desert", "drift", "echo", "ember", "falcon",
        "feather", "fjord", "forest", "fossil", "garden", "glacier", "grain", "harbor", "hazel", "hollow",
        "horizon", "island", "ivory", "jungle", "lagoon", "lantern", "lilac", "lotus", "maple", "marble",
        "meadow", "meteor", "mint", "mirage", "moon", "mosaic", "mountain", "nebula", "needle", "night",
        "oak", "oasis", "ocean", "olive", "opal", "orbit", "orchid", "panda", "pearl", "pebble",
        "pine", "planet", "plasma", "prairie", "pulse", "quartz", "quill", "rain", "raven", "reef",
        "river", "rocket", "rose", "saffron", "sail", "saturn", "scarlet", "shadow", "shore", "silver",
        "sky", "snow", "solar", "spruce", "star", "stone", "storm", "sun", "sunset", "tangent",
        "thunder", "tiger", "timber", "topaz", "trail", "tulip", "twilight", "valley", "velvet", "violet",
        "wave", "willow", "winter", "wolf", "zephyr", "zinc", "azure", "cliff", "dune", "elm"
    };

    private static class Base32
    {
        private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        public static string Encode(byte[] data)
        {
            if (data.Length == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder((data.Length + 4) / 5 * 8);
            var value = 0;
            var bits = 0;

            foreach (var b in data)
            {
                value = (value << 8) | b;
                bits += 8;
                while (bits >= 5)
                {
                    sb.Append(Alphabet[(value >> (bits - 5)) & 31]);
                    bits -= 5;
                }
            }

            if (bits > 0)
            {
                sb.Append(Alphabet[(value << (5 - bits)) & 31]);
            }

            return sb.ToString();
        }

        public static byte[] Decode(string input)
        {
            var clean = (input ?? string.Empty).Trim().TrimEnd('=').ToUpperInvariant();
            if (clean.Length == 0)
            {
                return Array.Empty<byte>();
            }

            var value = 0;
            var bits = 0;
            var output = new List<byte>((clean.Length * 5) / 8);

            foreach (var c in clean)
            {
                var index = Alphabet.IndexOf(c);
                if (index < 0)
                {
                    throw new FormatException("Invalid Base32 string.");
                }

                value = (value << 5) | index;
                bits += 5;
                if (bits >= 8)
                {
                    output.Add((byte)((value >> (bits - 8)) & 0xff));
                    bits -= 8;
                }
            }

            return output.ToArray();
        }
    }
}
