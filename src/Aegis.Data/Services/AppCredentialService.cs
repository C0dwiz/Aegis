using System.Security.Cryptography;
using Aegis.Common;
using Aegis.Data.Entities;
using Aegis.Data.Repositories;
using Microsoft.Extensions.Logging;

namespace Aegis.Data.Services;

public interface IAppCredentialService
{
    /// <summary>Register a new application and return its credentials.</summary>
    Task<AppCredential> CreateAppAsync(ulong ownerId, string appTitle, string shortName, string? description, string? website, string platform);

    /// <summary>Return all apps owned by the user.</summary>
    Task<IEnumerable<AppCredential>> GetUserAppsAsync(ulong ownerId);

    /// <summary>Return a specific app owned by the user (null if not found or not owner).</summary>
    Task<AppCredential?> GetAppAsync(int appId, ulong ownerId);

    /// <summary>Revoke a credential. Only the owner can do this.</summary>
    Task<bool> RevokeAppAsync(int appId, ulong ownerId);

    /// <summary>Validate credentials at connection time. Returns the credential on success.</summary>
    Task<AppCredential?> ValidateCredentialsAsync(int appId, string appHash);
}

public class AppCredentialService : IAppCredentialService
{
    private const int AppHashByteLength = 32; // 256-bit secret → 64-char hex
    private const int MaxAppsPerUser = 25;

    private readonly IAppCredentialRepository _repo;
    private readonly ILogger<AppCredentialService> _logger;

    public AppCredentialService(IAppCredentialRepository repo, ILogger<AppCredentialService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task<AppCredential> CreateAppAsync(
        ulong ownerId,
        string appTitle,
        string shortName,
        string? description,
        string? website,
        string platform)
    {
        appTitle = (appTitle ?? string.Empty).Trim();
        shortName = (shortName ?? string.Empty).Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(appTitle) || appTitle.Length < 2 || appTitle.Length > 64)
            throw new ArgumentException("App title must be 2-64 characters.", nameof(appTitle));

        if (!System.Text.RegularExpressions.Regex.IsMatch(shortName, @"^[a-z0-9][a-z0-9_]{1,31}$"))
            throw new ArgumentException(
                "Short name must be 2-32 lowercase letters, digits, or underscores and start with a letter or digit.",
                nameof(shortName));

        var normalizedPlatform = (platform ?? "other").Trim().ToLowerInvariant();
        if (normalizedPlatform is not ("android" or "ios" or "web" or "desktop" or "other"))
            normalizedPlatform = "other";

        var existing = await _repo.GetByOwnerAsync(ownerId);
        var activeCount = existing.Count(a => a.IsActive);
        if (activeCount >= MaxAppsPerUser)
            throw new InvalidOperationException($"Maximum of {MaxAppsPerUser} active applications per user reached.");

        var hashBytes = new byte[AppHashByteLength];
        RandomNumberGenerator.Fill(hashBytes);
        var appHash = Convert.ToHexString(hashBytes).ToLowerInvariant();

        var credential = new AppCredential
        {
            AppHash    = appHash,
            AppTitle   = appTitle,
            ShortName  = shortName,
            Description = description?.Trim(),
            Website    = website?.Trim(),
            Platform   = normalizedPlatform,
            OwnerId    = ownerId,
            IsActive   = true,
            CreatedAt  = DateTime.UtcNow
        };

        var created = await _repo.CreateAsync(credential);
        _logger.LogInformation(
            "App credential created: AppId={AppId} ShortName={ShortName} Owner={OwnerId}",
            created.AppId, created.ShortName, created.OwnerId);

        return created;
    }

    public async Task<IEnumerable<AppCredential>> GetUserAppsAsync(ulong ownerId)
        => await _repo.GetByOwnerAsync(ownerId);

    public async Task<AppCredential?> GetAppAsync(int appId, ulong ownerId)
    {
        var app = await _repo.GetByAppIdAsync(appId);
        if (app == null || app.OwnerId != ownerId)
            return null;
        return app;
    }

    public async Task<bool> RevokeAppAsync(int appId, ulong ownerId)
    {
        var revoked = await _repo.RevokeAsync(appId, ownerId);
        if (revoked)
        {
            _logger.LogInformation(
                "App credential revoked: AppId={AppId} by Owner={OwnerId}",
                appId, ownerId);
        }
        return revoked;
    }

    public async Task<AppCredential?> ValidateCredentialsAsync(int appId, string appHash)
    {
        if (appId == OfficialClientCredentials.AppId &&
            string.Equals(appHash, OfficialClientCredentials.AppHash, StringComparison.OrdinalIgnoreCase))
        {
            return new AppCredential
            {
                AppId = OfficialClientCredentials.AppId,
                AppHash = OfficialClientCredentials.AppHash,
                AppTitle = OfficialClientCredentials.AppTitle,
                ShortName = OfficialClientCredentials.ShortName,
                Platform = OfficialClientCredentials.Platform,
                OwnerId = 0,
                IsActive = true,
                CreatedAt = DateTime.UnixEpoch
            };
        }

        return await _repo.ValidateAsync(appId, appHash);
    }
}
