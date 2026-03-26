using System.Text.RegularExpressions;
using MessagePack;
using MessagePack.Resolvers;
using Aegis.Data.Entities;
using Aegis.Data.Repositories;
using Aegis.Common;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Group = Aegis.Data.Entities.Group;

namespace Aegis.Data.Services;

// ===================== USER REGISTRATION SERVICE =====================

public interface IUserRegistrationService
{
    Task<User> RegisterUserAsync(string username, string email, string password, string publicKey);
    Task<bool> IsUsernameAvailableAsync(string username);
    Task<bool> IsEmailAvailableAsync(string email);
}

public class UserRegistrationService : IUserRegistrationService
{
    private const string LegacyPublicKeyPrefix = "LEGACY_PUBLIC_KEY_";

    private readonly IUserRepository _userRepository;
    private readonly ICryptoProvider _cryptoProvider;
    private readonly ILogger<UserRegistrationService> _logger;
    private readonly Utils.FastIdGenerator _idGenerator;
    private readonly IUserSearchIndexService _searchIndex;

    public UserRegistrationService(
        IUserRepository userRepository,
        ICryptoProvider cryptoProvider,
        ILogger<UserRegistrationService> logger,
        Utils.FastIdGenerator? idGenerator = null,
        IUserSearchIndexService? searchIndex = null)
    {
        _userRepository = userRepository;
        _cryptoProvider = cryptoProvider;
        _logger = logger;
        _idGenerator = idGenerator ?? new Utils.FastIdGenerator(1);
        _searchIndex = searchIndex ?? new NoOpUserSearchIndexService();
    }

    internal static readonly Regex UsernameRegex = new(@"^[a-zA-Z0-9][a-zA-Z0-9_.-]{2,31}$", RegexOptions.Compiled);

    public async Task<User> RegisterUserAsync(string username, string email, string password, string publicKey)
    {
        username = username?.Trim() ?? string.Empty;
        email = NormalizeEmail(email);
        publicKey = NormalizePublicKey(publicKey, username);

        if (string.IsNullOrWhiteSpace(username) || username.Length < 3)
            throw new ArgumentException("Username must be at least 3 characters long", nameof(username));

        if (!UsernameRegex.IsMatch(username))
            throw new ArgumentException(
                "Username may only contain Latin letters (A-Z, a-z), digits, underscores, hyphens, and dots, and must start with a letter or digit",
                nameof(username));

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            throw new ArgumentException("Invalid email format", nameof(email));
        
        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
            throw new ArgumentException("Password must be at least 6 characters long", nameof(password));
        
        if (!await IsUsernameAvailableAsync(username))
            throw new InvalidOperationException("Username is already taken");
        
        if (!await IsEmailAvailableAsync(email))
            throw new InvalidOperationException("Email is already registered");

        var passwordHash = await _cryptoProvider.HashPasswordAsync(password);

        var user = new User
        {
            Id = (ulong)_idGenerator.NextId(),
            Username = username,
            Email = email,
            PasswordHash = passwordHash,
            PublicKey = publicKey,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var created = await _userRepository.CreateAsync(user);
        await _searchIndex.IndexUserAsync(created);
        _logger.LogInformation("User registered: {UserId} ({Username})", created.Id, created.Username);
        return created;
    }

    public async Task<bool> IsUsernameAvailableAsync(string username)
    {
        var existingUser = await _userRepository.GetByUsernameAsync((username ?? string.Empty).Trim());
        return existingUser == null;
    }

    public async Task<bool> IsEmailAvailableAsync(string email)
    {
        var existingUser = await _userRepository.GetByEmailAsync(NormalizeEmail(email));
        return existingUser == null;
    }

    public static string NormalizeEmail(string? email)
    {
        return (email ?? string.Empty).Trim().ToLowerInvariant();
    }

    public static string BuildLegacyEmail(string username)
    {
        var normalized = (username ?? string.Empty).Trim().ToLowerInvariant();
        return $"{normalized}@legacy.local";
    }

    public static string NormalizePublicKey(string? publicKey, string? username)
    {
        if (!string.IsNullOrWhiteSpace(publicKey))
        {
            return publicKey.Trim();
        }

        var normalizedUsername = (username ?? string.Empty).Trim();
        return $"{LegacyPublicKeyPrefix}{normalizedUsername.ToUpperInvariant()}";
    }
}

// ===================== USER AUTHENTICATION SERVICE =====================

public interface IUserAuthenticationService
{
    Task<(User User, Session Session)?> AuthenticateUserAsync(string username, string password, string clientInfo, string? ipAddress = null);
    Task<Session?> AuthenticateUserByTokenAsync(string token);
    Task<bool> ValidateSessionAsync(string token);
    Task<bool> LogoutAsync(string token);
}

public class UserAuthenticationService : IUserAuthenticationService
{
    private readonly IUserRepository _userRepository;
    private readonly ISessionRepository _sessionRepository;
    private readonly ICryptoProvider _cryptoProvider;

    public UserAuthenticationService(
        IUserRepository userRepository,
        ISessionRepository sessionRepository,
        ICryptoProvider cryptoProvider)
    {
        _userRepository = userRepository;
        _sessionRepository = sessionRepository;
        _cryptoProvider = cryptoProvider;
    }

    public async Task<(User User, Session Session)?> AuthenticateUserAsync(string username, string password, string clientInfo, string? ipAddress = null)
    {
        var user = await _userRepository.GetByUsernameAsync(username);
        if (user == null || !user.IsActive)
            return null;

        var isValidPassword = await _cryptoProvider.VerifyPasswordAsync(password, user.PasswordHash);
        if (!isValidPassword)
            return null;

        var sessionToken = GenerateSessionToken();
        var sessionKey = await _cryptoProvider.GenerateSessionKeyAsync();
        var sessionKeyHash = await _cryptoProvider.HashAsync(Convert.ToBase64String(sessionKey));

        var session = new Session
        {
            UserId = user.Id,
            SessionToken = sessionToken,
            SessionKeyHash = sessionKeyHash,
            ClientInfo = clientInfo,
            IpAddress = ipAddress,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            LastActivityAt = DateTime.UtcNow,
            IsActive = true
        };

        var createdSession = await _sessionRepository.CreateAsync(session);
        user.LastSeenAt = DateTime.UtcNow;
        await _userRepository.UpdateAsync(user);
        
        return (user, createdSession);
    }

    public async Task<Session?> AuthenticateUserByTokenAsync(string token)
    {
        var session = await _sessionRepository.GetByTokenAsync(token);
        if (session == null || !session.IsActive || session.ExpiresAt < DateTime.UtcNow)
            return null;

        session.LastActivityAt = DateTime.UtcNow;
        await _sessionRepository.UpdateAsync(session);

        return session;
    }

    public async Task<bool> ValidateSessionAsync(string token)
    {
        var session = await _sessionRepository.GetByTokenAsync(token);
        return session != null && session.IsActive && session.ExpiresAt >= DateTime.UtcNow;
    }

    public async Task<bool> LogoutAsync(string token)
    {
        var session = await _sessionRepository.GetByTokenAsync(token);
        if (session == null)
            return false;

        session.IsActive = false;
        await _sessionRepository.UpdateAsync(session);
        return true;
    }

    private string GenerateSessionToken()
    {
        return Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
    }
}

// ===================== USER SEARCH SERVICE =====================

public interface IUserSearchService
{
    Task<User?> FindUserByUsernameAsync(string username);
    Task<User?> FindUserByEmailAsync(string email);
    Task<IEnumerable<User>> SearchUsersByUsernameAsync(string pattern, int limit = 20);
    Task<IEnumerable<User>> SearchUsersAsync(string query, int limit = 20);
    Task<User?> FindUserByIdAsync(ulong userId);
}

public class UserSearchService : IUserSearchService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private static readonly MessagePackSerializerOptions MsgPackOptions =
        MessagePackSerializerOptions.Standard
            .WithResolver(ContractlessStandardResolver.Instance);

    private readonly IUserRepository _userRepository;
    private readonly IDistributedCache? _cache;
    private readonly IUserSearchIndexService _searchIndex;
    private readonly ILogger<UserSearchService> _logger;

    public UserSearchService(
        IUserRepository userRepository,
        IDistributedCache? cache,
        ILogger<UserSearchService> logger)
        : this(userRepository, cache, null, logger)
    {
    }

    public UserSearchService(
        IUserRepository userRepository,
        IDistributedCache? cache,
        IUserSearchIndexService? searchIndex,
        ILogger<UserSearchService> logger)
    {
        _userRepository = userRepository;
        _cache = cache;
        _searchIndex = searchIndex ?? new NoOpUserSearchIndexService();
        _logger = logger;
    }

    public UserSearchService(
        IUserRepository userRepository,
        ILogger<UserSearchService> logger)
        : this(userRepository, null, null, logger)
    {
    }

    public async Task<User?> FindUserByUsernameAsync(string username)
    {
        var normalized = (username ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        var cacheKey = $"user:by-username:{normalized.ToLowerInvariant()}";
        var cached = await TryGetUserFromCacheAsync(cacheKey);
        if (cached != null)
        {
            return cached;
        }

        var user = await _userRepository.GetByUsernameAsync(normalized);
        await TrySetUserCacheAsync(cacheKey, user);
        return user;
    }

    public async Task<User?> FindUserByEmailAsync(string email)
    {
        var normalized = (email ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        var cacheKey = $"user:by-email:{normalized}";
        var cached = await TryGetUserFromCacheAsync(cacheKey);
        if (cached != null)
        {
            return cached;
        }

        var user = await _userRepository.GetByEmailAsync(normalized);
        await TrySetUserCacheAsync(cacheKey, user);
        return user;
    }

    public async Task<IEnumerable<User>> SearchUsersByUsernameAsync(string pattern, int limit = 20)
    {
        var normalized = (pattern ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return Array.Empty<User>();
        }

        var safeLimit = Math.Clamp(limit, 1, 200);
        if (_searchIndex.IsEnabled)
        {
            var userIds = await _searchIndex.SearchUserIdsByUsernameAsync(normalized, safeLimit);
            if (userIds.Count > 0)
            {
                var usersById = new Dictionary<ulong, User>();
                foreach (var userId in userIds)
                {
                    var found = await _userRepository.GetByIdAsync(userId);
                    if (found != null)
                    {
                        usersById[userId] = found;
                    }
                }

                var ordered = userIds
                    .Where(usersById.ContainsKey)
                    .Select(id => usersById[id])
                    .Take(safeLimit)
                    .ToList();

                if (ordered.Count > 0)
                {
                    return ordered;
                }
            }
        }

        var users = await _userRepository.SearchByUsernameAsync(normalized);
        return users.Take(safeLimit);
    }

    public async Task<IEnumerable<User>> SearchUsersAsync(string query, int limit = 20)
    {
        return await _userRepository.SearchAsync(query, limit);
    }

    public async Task<User?> FindUserByIdAsync(ulong userId)
    {
        if (userId == 0)
        {
            return null;
        }

        var cacheKey = $"user:by-id:{userId}";
        var cached = await TryGetUserFromCacheAsync(cacheKey);
        if (cached != null)
        {
            return cached;
        }

        var user = await _userRepository.GetByIdAsync(userId);
        await TrySetUserCacheAsync(cacheKey, user);
        return user;
    }

    private async Task<User?> TryGetUserFromCacheAsync(string cacheKey)
    {
        if (_cache == null)
        {
            return null;
        }

        try
        {
            var bytes = await _cache.GetAsync(cacheKey);
            if (bytes == null || bytes.Length == 0)
            {
                return null;
            }

            return MessagePackSerializer.Deserialize<User>(bytes, MsgPackOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "User cache read failed for key {CacheKey}", cacheKey);
            return null;
        }
    }

    private async Task TrySetUserCacheAsync(string cacheKey, User? user)
    {
        if (_cache == null || user == null)
        {
            return;
        }

        try
        {
            var bytes = MessagePackSerializer.Serialize(ToCacheSafeUser(user), MsgPackOptions);
            await _cache.SetAsync(cacheKey, bytes, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheTtl
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "User cache write failed for key {CacheKey}", cacheKey);
        }
    }

    private static User ToCacheSafeUser(User source)
    {
        return new User
        {
            Id = source.Id,
            Username = source.Username,
            Email = source.Email,
            PublicKey = source.PublicKey,
            IdentityKeyFingerprint = source.IdentityKeyFingerprint,
            DisplayName = source.DisplayName,
            AvatarUrl = source.AvatarUrl,
            Bio = source.Bio,
            Location = source.Location,
            BirthDate = source.BirthDate,
            IsActive = source.IsActive,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt,
            LastSeenAt = source.LastSeenAt,
            PasswordHash = string.Empty
        };
    }
}

// ===================== USER PROFILE SERVICE =====================

public interface IUserProfileService
{
    Task<User?> GetProfileAsync(ulong userId);
    Task<User> UpdateProfileAsync(ulong userId, string? displayName, string? avatarUrl, string? bio, string? username, string? location, DateOnly? birthDate);
    Task<UserAvatar> AddAvatarAsync(ulong userId, string avatarUrl, bool makePrimary = false);
    Task<IReadOnlyList<UserAvatar>> GetAvatarsAsync(ulong userId);
    Task<bool> DeleteAvatarAsync(ulong userId, ulong avatarId);
    Task<bool> SetPrimaryAvatarAsync(ulong userId, ulong avatarId);
}

public class UserProfileService : IUserProfileService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private static readonly MessagePackSerializerOptions MsgPackOptions =
        MessagePackSerializerOptions.Standard
            .WithResolver(ContractlessStandardResolver.Instance);

    private readonly IUserRepository _userRepository;
    private readonly IUserAvatarRepository _avatarRepository;
    private readonly IAvatarStorageService _avatarStorage;
    private readonly IDistributedCache? _cache;
    private readonly IUserSearchIndexService _searchIndex;
    private readonly ILogger<UserProfileService> _logger;

    public UserProfileService(
        IUserRepository userRepository,
        IUserAvatarRepository avatarRepository,
        IAvatarStorageService avatarStorage,
        IDistributedCache? cache,
        IUserSearchIndexService? searchIndex,
        ILogger<UserProfileService> logger)
    {
        _userRepository = userRepository;
        _avatarRepository = avatarRepository;
        _avatarStorage = avatarStorage;
        _cache = cache;
        _searchIndex = searchIndex ?? new NoOpUserSearchIndexService();
        _logger = logger;
    }

    public UserProfileService(
        IUserRepository userRepository,
        IUserAvatarRepository avatarRepository,
        IAvatarStorageService avatarStorage,
        ILogger<UserProfileService> logger)
        : this(userRepository, avatarRepository, avatarStorage, null, null, logger)
    {
    }

    public UserProfileService(
        IUserRepository userRepository,
        IUserAvatarRepository avatarRepository,
        ILogger<UserProfileService> logger)
        : this(userRepository, avatarRepository, new PassThroughAvatarStorageService(), null, null, logger)
    {
    }

    public UserProfileService(
        IUserRepository userRepository,
        ILogger<UserProfileService> logger)
        : this(userRepository, new InMemoryAvatarRepository(), new PassThroughAvatarStorageService(), null, null, logger)
    {
    }

    public async Task<User?> GetProfileAsync(ulong userId)
    {
        if (userId == 0)
        {
            return null;
        }

        var cacheKey = $"profile:by-id:{userId}";
        if (_cache != null)
        {
            try
            {
                var bytes = await _cache.GetAsync(cacheKey);
                if (bytes != null)
                {
                    var cached = MessagePackSerializer.Deserialize<User>(bytes, MsgPackOptions);
                    if (cached != null)
                    {
                        return cached;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Profile cache read failed for user {UserId}", userId);
            }
        }

        var user = await _userRepository.GetByIdAsync(userId);

        if (_cache != null && user != null)
        {
            try
            {
                await _cache.SetAsync(
                    cacheKey,
                    MessagePackSerializer.Serialize(user, MsgPackOptions),
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Profile cache write failed for user {UserId}", userId);
            }
        }

        return user;
    }

    public async Task<User> UpdateProfileAsync(ulong userId, string? displayName, string? avatarUrl, string? bio, string? username, string? location, DateOnly? birthDate)
    {
        var user = await _userRepository.GetByIdAsync(userId)
            ?? throw new InvalidOperationException("User not found");

        var previousUsername = user.Username;
        var previousEmail = user.Email;

        if (username != null && username != user.Username)
        {
            username = username.Trim();
            if (username.Length < 3)
                throw new ArgumentException("Username must be at least 3 characters long");
            if (!UserRegistrationService.UsernameRegex.IsMatch(username))
                throw new ArgumentException(
                    "Username may only contain Latin letters (A-Z, a-z), digits, underscores, hyphens, and dots, and must start with a letter or digit");
            var existing = await _userRepository.GetByUsernameAsync(username);
            if (existing != null)
                throw new InvalidOperationException("Username is already taken");
            user.Username = username;
        }

        if (displayName != null) user.DisplayName = displayName.Trim();
        if (avatarUrl != null)
        {
            if (string.IsNullOrWhiteSpace(avatarUrl))
            {
                if (!string.IsNullOrWhiteSpace(user.AvatarUrl))
                {
                    await _avatarStorage.DeleteIfManagedAsync(user.AvatarUrl);
                }

                user.AvatarUrl = null;
            }
            else
            {
                var normalized = await _avatarStorage.NormalizeAvatarReferenceAsync(avatarUrl, userId);
                if (!string.Equals(normalized, user.AvatarUrl, StringComparison.Ordinal))
                {
                    await _avatarStorage.DeleteIfManagedAsync(user.AvatarUrl);
                }

                user.AvatarUrl = normalized;
            }
        }
        if (bio != null) user.Bio = string.IsNullOrWhiteSpace(bio) ? null : bio.Trim();
        if (location != null) user.Location = string.IsNullOrWhiteSpace(location) ? null : location.Trim();
        if (birthDate != null)
        {
            if (birthDate.Value > DateOnly.FromDateTime(DateTime.UtcNow))
                throw new ArgumentException("Birth date cannot be in the future");

            user.BirthDate = birthDate.Value;
        }
        user.UpdatedAt = DateTime.UtcNow;

        var updated = await _userRepository.UpdateAsync(user);
        await InvalidateProfileCacheAsync(updated, previousUsername, previousEmail);
        await _searchIndex.IndexUserAsync(updated);
        return updated;
    }

    public async Task<UserAvatar> AddAvatarAsync(ulong userId, string avatarUrl, bool makePrimary = false)
    {
        var user = await _userRepository.GetByIdAsync(userId)
            ?? throw new InvalidOperationException("User not found");

        var normalizedUrl = (avatarUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedUrl))
        {
            throw new ArgumentException("Avatar URL is required", nameof(avatarUrl));
        }

        normalizedUrl = await _avatarStorage.NormalizeAvatarReferenceAsync(normalizedUrl, userId);

        var existing = await _avatarRepository.GetByUserIdAsync(userId);
        var shouldBePrimary = makePrimary || !existing.Any();
        var created = await _avatarRepository.AddForUserAsync(userId, normalizedUrl, shouldBePrimary);

        if (shouldBePrimary)
        {
            if (!string.Equals(user.AvatarUrl, normalizedUrl, StringComparison.Ordinal))
            {
                await _avatarStorage.DeleteIfManagedAsync(user.AvatarUrl);
            }

            user.AvatarUrl = normalizedUrl;
            user.UpdatedAt = DateTime.UtcNow;
            var updated = await _userRepository.UpdateAsync(user);
            await InvalidateProfileCacheAsync(updated, updated.Username, updated.Email);
        }

        return created;
    }

    public async Task<IReadOnlyList<UserAvatar>> GetAvatarsAsync(ulong userId)
    {
        var avatars = await _avatarRepository.GetByUserIdAsync(userId);
        return avatars.ToList();
    }

    public async Task<bool> DeleteAvatarAsync(ulong userId, ulong avatarId)
    {
        var avatarsBefore = await _avatarRepository.GetByUserIdAsync(userId);
        var target = avatarsBefore.FirstOrDefault(a => a.Id == avatarId);

        var deleted = await _avatarRepository.DeleteForUserAsync(userId, avatarId);
        if (!deleted)
        {
            return false;
        }

        if (target != null)
        {
            await _avatarStorage.DeleteIfManagedAsync(target.AvatarUrl);
        }

        var user = await _userRepository.GetByIdAsync(userId);
        if (user != null)
        {
            var primary = await _avatarRepository.GetPrimaryByUserIdAsync(userId);
            user.AvatarUrl = primary?.AvatarUrl;
            user.UpdatedAt = DateTime.UtcNow;
            var updated = await _userRepository.UpdateAsync(user);
            await InvalidateProfileCacheAsync(updated, updated.Username, updated.Email);
        }

        return true;
    }

    public async Task<bool> SetPrimaryAvatarAsync(ulong userId, ulong avatarId)
    {
        var updatedPrimary = await _avatarRepository.SetPrimaryAsync(userId, avatarId);
        if (!updatedPrimary)
        {
            return false;
        }

        var user = await _userRepository.GetByIdAsync(userId);
        var primary = await _avatarRepository.GetPrimaryByUserIdAsync(userId);
        if (user != null)
        {
            user.AvatarUrl = primary?.AvatarUrl;
            user.UpdatedAt = DateTime.UtcNow;
            var updated = await _userRepository.UpdateAsync(user);
            await InvalidateProfileCacheAsync(updated, updated.Username, updated.Email);
        }

        return true;
    }

    private async Task InvalidateProfileCacheAsync(User user, string previousUsername, string previousEmail)
    {
        if (_cache == null)
        {
            return;
        }

        var keys = new HashSet<string>(StringComparer.Ordinal)
        {
            $"profile:by-id:{user.Id}",
            $"user:by-id:{user.Id}",
            $"user:by-username:{user.Username.ToLowerInvariant()}",
            $"user:by-email:{user.Email.ToLowerInvariant()}"
        };

        if (!string.Equals(previousUsername, user.Username, StringComparison.OrdinalIgnoreCase))
        {
            keys.Add($"user:by-username:{previousUsername.ToLowerInvariant()}");
        }

        if (!string.Equals(previousEmail, user.Email, StringComparison.OrdinalIgnoreCase))
        {
            keys.Add($"user:by-email:{previousEmail.ToLowerInvariant()}");
        }

        foreach (var key in keys)
        {
            try
            {
                await _cache.RemoveAsync(key);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Profile cache invalidation failed for key {CacheKey}", key);
            }
        }
    }
}

internal sealed class InMemoryAvatarRepository : IUserAvatarRepository
{
    private readonly List<UserAvatar> _items = new();
    private ulong _nextId = 1;

    public Task<UserAvatar?> GetByIdAsync(ulong id) => Task.FromResult(_items.FirstOrDefault(x => x.Id == id));
    public Task<IEnumerable<UserAvatar>> GetAllAsync() => Task.FromResult<IEnumerable<UserAvatar>>(_items.ToList());
    public Task<IEnumerable<UserAvatar>> FindAsync(System.Linq.Expressions.Expression<Func<UserAvatar, bool>> predicate)
        => Task.FromResult<IEnumerable<UserAvatar>>(_items.AsQueryable().Where(predicate).ToList());
    public Task<UserAvatar> CreateAsync(UserAvatar entity)
    {
        entity.Id = _nextId++;
        _items.Add(entity);
        return Task.FromResult(entity);
    }
    public Task<UserAvatar> UpdateAsync(UserAvatar entity)
    {
        var idx = _items.FindIndex(x => x.Id == entity.Id);
        if (idx >= 0) _items[idx] = entity;
        return Task.FromResult(entity);
    }
    public Task<bool> DeleteAsync(ulong id)
    {
        var removed = _items.RemoveAll(x => x.Id == id) > 0;
        return Task.FromResult(removed);
    }

    public Task<IEnumerable<UserAvatar>> GetByUserIdAsync(ulong userId)
        => Task.FromResult<IEnumerable<UserAvatar>>(_items.Where(a => a.UserId == userId).OrderByDescending(a => a.IsPrimary).ThenByDescending(a => a.CreatedAt).ToList());

    public Task<UserAvatar?> GetPrimaryByUserIdAsync(ulong userId)
        => Task.FromResult(_items.Where(a => a.UserId == userId && a.IsPrimary).OrderByDescending(a => a.CreatedAt).FirstOrDefault());

    public async Task<UserAvatar> AddForUserAsync(ulong userId, string avatarUrl, bool makePrimary)
    {
        if (makePrimary)
        {
            foreach (var avatar in _items.Where(a => a.UserId == userId))
            {
                avatar.IsPrimary = false;
            }
        }

        var entity = new UserAvatar
        {
            Id = _nextId++,
            UserId = userId,
            AvatarUrl = avatarUrl,
            IsPrimary = makePrimary,
            CreatedAt = DateTime.UtcNow
        };
        _items.Add(entity);
        return await Task.FromResult(entity);
    }

    public Task<bool> DeleteForUserAsync(ulong userId, ulong avatarId)
    {
        var removed = _items.RemoveAll(a => a.UserId == userId && a.Id == avatarId) > 0;
        return Task.FromResult(removed);
    }

    public Task<bool> SetPrimaryAsync(ulong userId, ulong avatarId)
    {
        var target = _items.FirstOrDefault(a => a.UserId == userId && a.Id == avatarId);
        if (target == null)
        {
            return Task.FromResult(false);
        }

        foreach (var avatar in _items.Where(a => a.UserId == userId))
        {
            avatar.IsPrimary = avatar.Id == avatarId;
        }

        return Task.FromResult(true);
    }
}

// ===================== CHANNEL SERVICE =====================

public interface IChannelService
{
    Task<Channel> CreateChannelAsync(ulong creatorUserId, string name, string? description, ChannelType type);
    Task<(Channel Channel, bool WasAlreadyMember)> JoinChannelAsync(ulong userId, ulong channelId);
    Task<(Channel Channel, bool WasAlreadyMember)> JoinChannelByInviteCodeAsync(ulong userId, string inviteCode);
    Task<Channel> UpdateChannelAsync(ulong channelId, ulong userId, string? name, string? description, string? avatarUrl);
    Task<Channel> UpdateChannelLinksAsync(ulong channelId, ulong userId, string? publicAlias, bool regeneratePrivateInvite);
    Task<string> GetInviteLinkAsync(ulong channelId, ulong userId);
    Task<string?> GetPublicLinkAsync(ulong channelId);
    Task<Channel?> ResolveByLinkAsync(string linkOrAlias);
    Task<ChannelMember> UpdateMemberRoleAsync(ulong channelId, ulong actorUserId, ulong targetUserId, ChannelMemberRole newRole);
    Task<ChannelMember> UpdateMemberPermissionsAsync(ulong channelId, ulong actorUserId, ulong targetUserId, MemberPermissions permissions);
    Task<bool> HasPermissionAsync(ulong channelId, ulong userId, string permission);
}

public record MemberPermissions(
    bool? CanSendMessages = null,
    bool? CanDeleteOthersMessages = null,
    bool? CanEditChannelInfo = null,
    bool? CanInviteUsers = null,
    bool? CanRemoveUsers = null,
    bool? CanPinMessages = null,
    bool? CanManageRoles = null
);

public class ChannelService : IChannelService
{
    private readonly IChannelRepository _channelRepository;
    private readonly Utils.FastIdGenerator _idGenerator;

    public ChannelService(IChannelRepository channelRepository)
    {
        _channelRepository = channelRepository;
        // TODO: Передавать idGenerator через DI
        _idGenerator = new Utils.FastIdGenerator(1); // nodeId=1 временно
    }

    public async Task<Channel> CreateChannelAsync(ulong creatorUserId, string name, string? description, ChannelType type)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length < 2)
            throw new ArgumentException("Channel name must be at least 2 characters long");

        var channel = new Channel
        {
            Id = (ulong)_idGenerator.NextId(),
            Name = name,
            Description = description,
            Type = type,
            CreatedByUserId = creatorUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsActive = true,
            MemberCount = 1,
            InviteCode = CreateInviteCode(),
            PublicAlias = null
        };

        var created = await _channelRepository.CreateAsync(channel);

        // Add creator as owner with all permissions
        var ownerMember = new ChannelMember
        {
            ChannelId = created.Id,
            UserId = creatorUserId,
            Role = ChannelMemberRole.Owner,
            JoinedAt = DateTime.UtcNow,
            IsActive = true,
            CanSendMessages = true,
            CanDeleteOthersMessages = true,
            CanEditChannelInfo = true,
            CanInviteUsers = true,
            CanRemoveUsers = true,
            CanPinMessages = true,
            CanManageRoles = true
        };
        await _channelRepository.AddMemberAsync(ownerMember);

        return created;
    }

    public async Task<(Channel Channel, bool WasAlreadyMember)> JoinChannelAsync(ulong userId, ulong channelId)
    {
        var channel = await _channelRepository.GetByIdAsync(channelId)
            ?? throw new InvalidOperationException("Channel not found");

        if (!channel.IsActive)
            throw new InvalidOperationException("Channel is not active");

        if (channel.Type == ChannelType.Private)
            throw new UnauthorizedAccessException("Cannot join a private channel without an invitation");

        var existing = await _channelRepository.GetChannelMemberAsync(channelId, userId);
        if (existing != null)
            return (channel, true);

        var member = new ChannelMember
        {
            ChannelId = channelId,
            UserId = userId,
            Role = ChannelMemberRole.Member,
            JoinedAt = DateTime.UtcNow,
            IsActive = true,
            CanSendMessages = true
        };
        await _channelRepository.AddMemberAsync(member);

        channel.MemberCount += 1;
        channel.UpdatedAt = DateTime.UtcNow;
        await _channelRepository.UpdateAsync(channel);

        return (channel, false);
    }

    public async Task<(Channel Channel, bool WasAlreadyMember)> JoinChannelByInviteCodeAsync(ulong userId, string inviteCode)
    {
        var channel = await _channelRepository.GetByInviteCodeAsync(inviteCode)
            ?? throw new InvalidOperationException("Channel not found");

        return await JoinChannelAllowPrivateAsync(userId, channel);
    }

    public async Task<Channel> UpdateChannelAsync(ulong channelId, ulong userId, string? name, string? description, string? avatarUrl)
    {
        var member = await _channelRepository.GetChannelMemberAsync(channelId, userId)
            ?? throw new InvalidOperationException("Not a member of this channel");

        if (!CanEditInfo(member))
            throw new UnauthorizedAccessException("No permission to edit channel info");

        var channel = await _channelRepository.GetByIdAsync(channelId)
            ?? throw new InvalidOperationException("Channel not found");

        if (name != null) channel.Name = name;
        if (description != null) channel.Description = description;
        if (avatarUrl != null) channel.AvatarUrl = avatarUrl;
        channel.UpdatedAt = DateTime.UtcNow;

        return await _channelRepository.UpdateAsync(channel);
    }

    public async Task<Channel> UpdateChannelLinksAsync(ulong channelId, ulong userId, string? publicAlias, bool regeneratePrivateInvite)
    {
        var member = await _channelRepository.GetChannelMemberAsync(channelId, userId)
            ?? throw new InvalidOperationException("Not a member of this channel");

        if (!CanEditInfo(member))
            throw new UnauthorizedAccessException("No permission to edit channel links");

        var channel = await _channelRepository.GetByIdAsync(channelId)
            ?? throw new InvalidOperationException("Channel not found");

        if (publicAlias != null)
        {
            var normalizedAlias = NormalizeAlias(publicAlias);
            if (normalizedAlias.Length == 0)
            {
                channel.PublicAlias = null;
            }
            else
            {
                if (!Regex.IsMatch(normalizedAlias, "^[a-zA-Z0-9_]{4,32}$"))
                {
                    throw new ArgumentException("Public alias must be 4-32 chars and contain only letters, digits, and underscores");
                }

                var taken = await _channelRepository.IsPublicAliasTakenAsync(normalizedAlias, channelId);
                if (taken)
                {
                    throw new InvalidOperationException("Alias is already taken");
                }

                channel.PublicAlias = normalizedAlias;
            }
        }

        if (regeneratePrivateInvite)
        {
            channel.InviteCode = CreateInviteCode();
        }

        channel.UpdatedAt = DateTime.UtcNow;
        return await _channelRepository.UpdateAsync(channel);
    }

    public async Task<string> GetInviteLinkAsync(ulong channelId, ulong userId)
    {
        var member = await _channelRepository.GetChannelMemberAsync(channelId, userId)
            ?? throw new InvalidOperationException("Not a member of this channel");

        if (!member.CanInviteUsers && member.Role != ChannelMemberRole.Owner)
            throw new UnauthorizedAccessException("No permission to view invite link");

        var channel = await _channelRepository.GetByIdAsync(channelId)
            ?? throw new InvalidOperationException("Channel not found");

        if (string.IsNullOrWhiteSpace(channel.InviteCode))
        {
            channel.InviteCode = CreateInviteCode();
            channel = await _channelRepository.UpdateAsync(channel);
        }

        return $"aegis://join/{channel.InviteCode}";
    }

    public async Task<string?> GetPublicLinkAsync(ulong channelId)
    {
        var channel = await _channelRepository.GetByIdAsync(channelId)
            ?? throw new InvalidOperationException("Channel not found");

        if (string.IsNullOrWhiteSpace(channel.PublicAlias))
        {
            return null;
        }

        return $"@{channel.PublicAlias}";
    }

    public async Task<Channel?> ResolveByLinkAsync(string linkOrAlias)
    {
        var value = (linkOrAlias ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value.StartsWith("aegis://join/", StringComparison.OrdinalIgnoreCase))
        {
            var inviteCode = value["aegis://join/".Length..].Trim();
            return await _channelRepository.GetByInviteCodeAsync(inviteCode);
        }

        var alias = NormalizeAlias(value);
        var byAlias = await _channelRepository.GetByPublicAliasAsync(alias);
        if (byAlias != null)
        {
            return byAlias;
        }

        return await _channelRepository.GetByInviteCodeAsync(value);
    }

    public async Task<ChannelMember> UpdateMemberRoleAsync(ulong channelId, ulong actorUserId, ulong targetUserId, ChannelMemberRole newRole)
    {
        var actor = await _channelRepository.GetChannelMemberAsync(channelId, actorUserId)
            ?? throw new InvalidOperationException("Actor is not a member");

        if (!actor.CanManageRoles && actor.Role != ChannelMemberRole.Owner)
            throw new UnauthorizedAccessException("No permission to manage roles");

        // Cannot change owner role unless you're the owner
        if (newRole == ChannelMemberRole.Owner && actor.Role != ChannelMemberRole.Owner)
            throw new UnauthorizedAccessException("Only owner can transfer ownership");

        var target = await _channelRepository.GetChannelMemberAsync(channelId, targetUserId)
            ?? throw new InvalidOperationException("Target user is not a member");

        // Cannot demote someone with higher or equal role (unless you're owner)
        if (actor.Role != ChannelMemberRole.Owner && target.Role >= actor.Role)
            throw new UnauthorizedAccessException("Cannot modify role of member with equal or higher role");

        target.Role = newRole;
        // Set default permissions based on role
        ApplyDefaultPermissions(target, newRole);

        return await _channelRepository.UpdateMemberAsync(target);
    }

    public async Task<ChannelMember> UpdateMemberPermissionsAsync(ulong channelId, ulong actorUserId, ulong targetUserId, MemberPermissions permissions)
    {
        var actor = await _channelRepository.GetChannelMemberAsync(channelId, actorUserId)
            ?? throw new InvalidOperationException("Actor is not a member");

        if (!actor.CanManageRoles && actor.Role != ChannelMemberRole.Owner)
            throw new UnauthorizedAccessException("No permission to manage permissions");

        var target = await _channelRepository.GetChannelMemberAsync(channelId, targetUserId)
            ?? throw new InvalidOperationException("Target user is not a member");

        if (actor.Role != ChannelMemberRole.Owner && target.Role >= actor.Role)
            throw new UnauthorizedAccessException("Cannot modify permissions of member with equal or higher role");

        if (permissions.CanSendMessages.HasValue) target.CanSendMessages = permissions.CanSendMessages.Value;
        if (permissions.CanDeleteOthersMessages.HasValue) target.CanDeleteOthersMessages = permissions.CanDeleteOthersMessages.Value;
        if (permissions.CanEditChannelInfo.HasValue) target.CanEditChannelInfo = permissions.CanEditChannelInfo.Value;
        if (permissions.CanInviteUsers.HasValue) target.CanInviteUsers = permissions.CanInviteUsers.Value;
        if (permissions.CanRemoveUsers.HasValue) target.CanRemoveUsers = permissions.CanRemoveUsers.Value;
        if (permissions.CanPinMessages.HasValue) target.CanPinMessages = permissions.CanPinMessages.Value;
        if (permissions.CanManageRoles.HasValue) target.CanManageRoles = permissions.CanManageRoles.Value;

        return await _channelRepository.UpdateMemberAsync(target);
    }

    public async Task<bool> HasPermissionAsync(ulong channelId, ulong userId, string permission)
    {
        var member = await _channelRepository.GetChannelMemberAsync(channelId, userId);
        if (member == null) return false;
        if (member.Role == ChannelMemberRole.Owner) return true;

        return permission switch
        {
            "send_messages" => member.CanSendMessages,
            "delete_others_messages" => member.CanDeleteOthersMessages,
            "edit_channel_info" => member.CanEditChannelInfo,
            "invite_users" => member.CanInviteUsers,
            "remove_users" => member.CanRemoveUsers,
            "pin_messages" => member.CanPinMessages,
            "manage_roles" => member.CanManageRoles,
            _ => false
        };
    }

    private bool CanEditInfo(ChannelMember member)
    {
        return member.Role == ChannelMemberRole.Owner || member.CanEditChannelInfo;
    }

    private void ApplyDefaultPermissions(ChannelMember member, ChannelMemberRole role)
    {
        switch (role)
        {
            case ChannelMemberRole.Owner:
                member.CanSendMessages = true;
                member.CanDeleteOthersMessages = true;
                member.CanEditChannelInfo = true;
                member.CanInviteUsers = true;
                member.CanRemoveUsers = true;
                member.CanPinMessages = true;
                member.CanManageRoles = true;
                break;
            case ChannelMemberRole.Admin:
                member.CanSendMessages = true;
                member.CanDeleteOthersMessages = true;
                member.CanEditChannelInfo = true;
                member.CanInviteUsers = true;
                member.CanRemoveUsers = true;
                member.CanPinMessages = true;
                member.CanManageRoles = false;
                break;
            case ChannelMemberRole.Moderator:
                member.CanSendMessages = true;
                member.CanDeleteOthersMessages = true;
                member.CanEditChannelInfo = false;
                member.CanInviteUsers = true;
                member.CanRemoveUsers = false;
                member.CanPinMessages = true;
                member.CanManageRoles = false;
                break;
            case ChannelMemberRole.Member:
                member.CanSendMessages = true;
                member.CanDeleteOthersMessages = false;
                member.CanEditChannelInfo = false;
                member.CanInviteUsers = false;
                member.CanRemoveUsers = false;
                member.CanPinMessages = false;
                member.CanManageRoles = false;
                break;
        }
    }

    private async Task<(Channel Channel, bool WasAlreadyMember)> JoinChannelAllowPrivateAsync(ulong userId, Channel channel)
    {
        var existing = await _channelRepository.GetChannelMemberAsync(channel.Id, userId);
        if (existing != null)
        {
            return (channel, true);
        }

        var member = new ChannelMember
        {
            ChannelId = channel.Id,
            UserId = userId,
            Role = ChannelMemberRole.Member,
            JoinedAt = DateTime.UtcNow,
            IsActive = true,
            CanSendMessages = true
        };
        await _channelRepository.AddMemberAsync(member);

        channel.MemberCount += 1;
        channel.UpdatedAt = DateTime.UtcNow;
        await _channelRepository.UpdateAsync(channel);

        return (channel, false);
    }

    private static string CreateInviteCode()
    {
        return Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant();
    }

    private static string NormalizeAlias(string value)
    {
        return (value ?? string.Empty).Trim().TrimStart('@');
    }

}

// ===================== GROUP SERVICE =====================

public interface IGroupService
{
    Task<Group> CreateGroupAsync(ulong creatorUserId, string name, string? description);
    Task<Group> UpdateGroupAsync(ulong groupId, ulong userId, string? name, string? description, string? avatarUrl);
    Task<GroupMember> UpdateMemberRoleAsync(ulong groupId, ulong actorUserId, ulong targetUserId, GroupMemberRole newRole);
    Task<GroupMember> UpdateMemberPermissionsAsync(ulong groupId, ulong actorUserId, ulong targetUserId, MemberPermissions permissions);
}

public class GroupService : IGroupService
{
    private readonly IGroupRepository _groupRepository;
    private readonly Utils.FastIdGenerator _idGenerator;

    public GroupService(IGroupRepository groupRepository)
    {
        _groupRepository = groupRepository;
        // TODO: Передавать idGenerator через DI
        _idGenerator = new Utils.FastIdGenerator(1); // nodeId=1 временно
    }

    public async Task<Group> CreateGroupAsync(ulong creatorUserId, string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length < 2)
            throw new ArgumentException("Group name must be at least 2 characters long");

        var group = new Group
        {
            Id = (ulong)_idGenerator.NextId(),
            Name = name,
            Description = description,
            CreatedByUserId = creatorUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsActive = true,
            MemberCount = 1
        };

        var created = await _groupRepository.CreateAsync(group);

        var ownerMember = new GroupMember
        {
            GroupId = created.Id,
            UserId = creatorUserId,
            Role = GroupMemberRole.Owner,
            JoinedAt = DateTime.UtcNow,
            IsActive = true,
            CanSendMessages = true,
            CanDeleteOthersMessages = true,
            CanEditGroupInfo = true,
            CanInviteUsers = true,
            CanRemoveUsers = true,
            CanPinMessages = true,
            CanManageRoles = true
        };
        await _groupRepository.AddMemberAsync(ownerMember);

        return created;
    }

    public async Task<Group> UpdateGroupAsync(ulong groupId, ulong userId, string? name, string? description, string? avatarUrl)
    {
        var member = await _groupRepository.GetGroupMemberAsync(groupId, userId)
            ?? throw new InvalidOperationException("Not a member of this group");

        if (member.Role != GroupMemberRole.Owner && !member.CanEditGroupInfo)
            throw new UnauthorizedAccessException("No permission to edit group info");

        var group = await _groupRepository.GetByIdAsync(groupId)
            ?? throw new InvalidOperationException("Group not found");

        if (name != null) group.Name = name;
        if (description != null) group.Description = description;
        if (avatarUrl != null) group.AvatarUrl = avatarUrl;
        group.UpdatedAt = DateTime.UtcNow;

        return await _groupRepository.UpdateAsync(group);
    }

    public async Task<GroupMember> UpdateMemberRoleAsync(ulong groupId, ulong actorUserId, ulong targetUserId, GroupMemberRole newRole)
    {
        var actor = await _groupRepository.GetGroupMemberAsync(groupId, actorUserId)
            ?? throw new InvalidOperationException("Actor is not a member");

        if (!actor.CanManageRoles && actor.Role != GroupMemberRole.Owner)
            throw new UnauthorizedAccessException("No permission to manage roles");

        if (newRole == GroupMemberRole.Owner && actor.Role != GroupMemberRole.Owner)
            throw new UnauthorizedAccessException("Only owner can transfer ownership");

        var target = await _groupRepository.GetGroupMemberAsync(groupId, targetUserId)
            ?? throw new InvalidOperationException("Target user is not a member");

        if (actor.Role != GroupMemberRole.Owner && target.Role >= actor.Role)
            throw new UnauthorizedAccessException("Cannot modify role of member with equal or higher role");

        target.Role = newRole;
        ApplyDefaultGroupPermissions(target, newRole);

        return await _groupRepository.UpdateMemberAsync(target);
    }

    public async Task<GroupMember> UpdateMemberPermissionsAsync(ulong groupId, ulong actorUserId, ulong targetUserId, MemberPermissions permissions)
    {
        var actor = await _groupRepository.GetGroupMemberAsync(groupId, actorUserId)
            ?? throw new InvalidOperationException("Actor is not a member");

        if (!actor.CanManageRoles && actor.Role != GroupMemberRole.Owner)
            throw new UnauthorizedAccessException("No permission to manage permissions");

        var target = await _groupRepository.GetGroupMemberAsync(groupId, targetUserId)
            ?? throw new InvalidOperationException("Target user is not a member");

        if (actor.Role != GroupMemberRole.Owner && target.Role >= actor.Role)
            throw new UnauthorizedAccessException("Cannot modify permissions of member with equal or higher role");

        if (permissions.CanSendMessages.HasValue) target.CanSendMessages = permissions.CanSendMessages.Value;
        if (permissions.CanDeleteOthersMessages.HasValue) target.CanDeleteOthersMessages = permissions.CanDeleteOthersMessages.Value;
        if (permissions.CanEditChannelInfo.HasValue) target.CanEditGroupInfo = permissions.CanEditChannelInfo.Value;
        if (permissions.CanInviteUsers.HasValue) target.CanInviteUsers = permissions.CanInviteUsers.Value;
        if (permissions.CanRemoveUsers.HasValue) target.CanRemoveUsers = permissions.CanRemoveUsers.Value;
        if (permissions.CanPinMessages.HasValue) target.CanPinMessages = permissions.CanPinMessages.Value;
        if (permissions.CanManageRoles.HasValue) target.CanManageRoles = permissions.CanManageRoles.Value;

        return await _groupRepository.UpdateMemberAsync(target);
    }

    private void ApplyDefaultGroupPermissions(GroupMember member, GroupMemberRole role)
    {
        switch (role)
        {
            case GroupMemberRole.Owner:
                member.CanSendMessages = true;
                member.CanDeleteOthersMessages = true;
                member.CanEditGroupInfo = true;
                member.CanInviteUsers = true;
                member.CanRemoveUsers = true;
                member.CanPinMessages = true;
                member.CanManageRoles = true;
                break;
            case GroupMemberRole.Admin:
                member.CanSendMessages = true;
                member.CanDeleteOthersMessages = true;
                member.CanEditGroupInfo = true;
                member.CanInviteUsers = true;
                member.CanRemoveUsers = true;
                member.CanPinMessages = true;
                member.CanManageRoles = false;
                break;
            case GroupMemberRole.Moderator:
                member.CanSendMessages = true;
                member.CanDeleteOthersMessages = true;
                member.CanEditGroupInfo = false;
                member.CanInviteUsers = true;
                member.CanRemoveUsers = false;
                member.CanPinMessages = true;
                member.CanManageRoles = false;
                break;
            case GroupMemberRole.Member:
                member.CanSendMessages = true;
                member.CanDeleteOthersMessages = false;
                member.CanEditGroupInfo = false;
                member.CanInviteUsers = false;
                member.CanRemoveUsers = false;
                member.CanPinMessages = false;
                member.CanManageRoles = false;
                break;
        }
    }
}

// ===================== MESSAGE SERVICE =====================

public interface IMessageService
{
    // Private messages
    Task<Message> SendPrivateMessageAsync(ulong fromUserId, ulong toUserId, string content, MessageContentType contentType = MessageContentType.Text);
    Task<Message> EditMessageAsync(ulong messageId, ulong userId, string newContent);
    Task<bool> DeleteMessageAsync(ulong messageId, ulong userId);
    
    // Channel messages
    Task<ChannelMessage> SendChannelMessageAsync(ulong channelId, ulong fromUserId, string content, MessageContentType contentType = MessageContentType.Text, ulong? replyToId = null);
    Task<ChannelMessage> EditChannelMessageAsync(ulong messageId, ulong userId, ulong channelId, string newContent);
    Task<bool> DeleteChannelMessageAsync(ulong messageId, ulong userId, ulong channelId);
    
    // Group messages
    Task<GroupMessage> SendGroupMessageAsync(ulong groupId, ulong fromUserId, string content, MessageContentType contentType = MessageContentType.Text, ulong? replyToId = null);
    Task<GroupMessage> EditGroupMessageAsync(ulong messageId, ulong userId, ulong groupId, string newContent);
    Task<bool> DeleteGroupMessageAsync(ulong messageId, ulong userId, ulong groupId);
}

public class MessageService : IMessageService
{
    private readonly IMessageRepository _messageRepository;
    private readonly IChannelRepository _channelRepository;
    private readonly IGroupRepository _groupRepository;
    private readonly IPrivateChatRepository _privateChatRepository;

    public MessageService(
        IMessageRepository messageRepository,
        IChannelRepository channelRepository,
        IGroupRepository groupRepository,
        IPrivateChatRepository privateChatRepository)
    {
        _messageRepository = messageRepository;
        _channelRepository = channelRepository;
        _groupRepository = groupRepository;
        _privateChatRepository = privateChatRepository;
    }

    // ---- Private Messages ----

    public async Task<Message> SendPrivateMessageAsync(ulong fromUserId, ulong toUserId, string content, MessageContentType contentType = MessageContentType.Text)
    {
        if (string.IsNullOrWhiteSpace(content) && contentType == MessageContentType.Text)
            throw new ArgumentException("Message content cannot be empty");

        content ??= string.Empty;

        // Get or create private chat
        var chat = await _privateChatRepository.GetPrivateChatAsync(fromUserId, toUserId);
        if (chat == null)
            chat = await _privateChatRepository.CreatePrivateChatAsync(fromUserId, toUserId);

        var message = new Message
        {
            FromUserId = fromUserId,
            ToUserId = toUserId,
            Content = content,
            ContentType = contentType,
            CreatedAt = DateTime.UtcNow
        };

        var created = await _messageRepository.CreateAsync(message);

        chat.LastMessageId = created.Id;
        chat.LastActivityAt = DateTime.UtcNow;
        await _privateChatRepository.UpdateAsync(chat);

        return created;
    }

    public async Task<Message> EditMessageAsync(ulong messageId, ulong userId, string newContent)
    {
        if (string.IsNullOrWhiteSpace(newContent))
            throw new ArgumentException("Message content cannot be empty");

        var message = await _messageRepository.GetMessageForEditAsync(messageId, userId)
            ?? throw new InvalidOperationException("Message not found or you don't have permission to edit it");

        message.Content = newContent;
        message.IsEdited = true;
        message.EditedAt = DateTime.UtcNow;

        return await _messageRepository.UpdateAsync(message);
    }

    public async Task<bool> DeleteMessageAsync(ulong messageId, ulong userId)
    {
        var message = await _messageRepository.GetByIdAsync(messageId);
        if (message == null || message.IsDeleted) return false;

        // Only the sender can delete
        if (message.FromUserId != userId) return false;

        message.IsDeleted = true;
        message.DeletedAt = DateTime.UtcNow;
        message.Content = string.Empty; // Clear content
        await _messageRepository.UpdateAsync(message);
        return true;
    }

    // ---- Channel Messages ----

    public async Task<ChannelMessage> SendChannelMessageAsync(ulong channelId, ulong fromUserId, string content, MessageContentType contentType = MessageContentType.Text, ulong? replyToId = null)
    {
        if (string.IsNullOrWhiteSpace(content) && contentType == MessageContentType.Text)
            throw new ArgumentException("Message content cannot be empty");

        content ??= string.Empty;

        var member = await _channelRepository.GetChannelMemberAsync(channelId, fromUserId)
            ?? throw new InvalidOperationException("Not a member of this channel");

        if (!member.CanSendMessages && member.Role != ChannelMemberRole.Owner)
            throw new UnauthorizedAccessException("No permission to send messages");

        var msg = new ChannelMessage
        {
            ChannelId = channelId,
            FromUserId = fromUserId,
            Content = content,
            ContentType = contentType,
            CreatedAt = DateTime.UtcNow,
            ReplyToMessageId = replyToId
        };

        return await _channelRepository.AddChannelMessageAsync(msg);
    }

    public async Task<ChannelMessage> EditChannelMessageAsync(ulong messageId, ulong userId, ulong channelId, string newContent)
    {
        if (string.IsNullOrWhiteSpace(newContent))
            throw new ArgumentException("Message content cannot be empty");

        var msg = await _channelRepository.GetChannelMessageAsync(messageId)
            ?? throw new InvalidOperationException("Message not found");

        // Only the author can edit their own message
        if (msg.FromUserId != userId)
            throw new UnauthorizedAccessException("Can only edit your own messages");

        msg.Content = newContent;
        msg.IsEdited = true;
        msg.EditedAt = DateTime.UtcNow;

        return await _channelRepository.UpdateChannelMessageAsync(msg);
    }

    public async Task<bool> DeleteChannelMessageAsync(ulong messageId, ulong userId, ulong channelId)
    {
        var msg = await _channelRepository.GetChannelMessageAsync(messageId);
        if (msg == null) return false;

        // Author can delete their own messages
        if (msg.FromUserId == userId)
        {
            msg.IsDeleted = true;
            msg.DeletedAt = DateTime.UtcNow;
            msg.Content = string.Empty;
            await _channelRepository.UpdateChannelMessageAsync(msg);
            return true;
        }

        // Admins/moderators with permission can delete others' messages
        var member = await _channelRepository.GetChannelMemberAsync(channelId, userId);
        if (member != null && (member.Role == ChannelMemberRole.Owner || member.CanDeleteOthersMessages))
        {
            msg.IsDeleted = true;
            msg.DeletedAt = DateTime.UtcNow;
            msg.Content = string.Empty;
            await _channelRepository.UpdateChannelMessageAsync(msg);
            return true;
        }

        return false;
    }

    // ---- Group Messages ----

    public async Task<GroupMessage> SendGroupMessageAsync(ulong groupId, ulong fromUserId, string content, MessageContentType contentType = MessageContentType.Text, ulong? replyToId = null)
    {
        if (string.IsNullOrWhiteSpace(content) && contentType == MessageContentType.Text)
            throw new ArgumentException("Message content cannot be empty");

        content ??= string.Empty;

        var member = await _groupRepository.GetGroupMemberAsync(groupId, fromUserId)
            ?? throw new InvalidOperationException("Not a member of this group");

        if (!member.CanSendMessages && member.Role != GroupMemberRole.Owner)
            throw new UnauthorizedAccessException("No permission to send messages");

        var msg = new GroupMessage
        {
            GroupId = groupId,
            FromUserId = fromUserId,
            Content = content,
            ContentType = contentType,
            CreatedAt = DateTime.UtcNow,
            ReplyToMessageId = replyToId
        };

        return await _groupRepository.AddGroupMessageAsync(msg);
    }

    public async Task<GroupMessage> EditGroupMessageAsync(ulong messageId, ulong userId, ulong groupId, string newContent)
    {
        if (string.IsNullOrWhiteSpace(newContent))
            throw new ArgumentException("Message content cannot be empty");

        var msg = await _groupRepository.GetGroupMessageAsync(messageId)
            ?? throw new InvalidOperationException("Message not found");

        if (msg.FromUserId != userId)
            throw new UnauthorizedAccessException("Can only edit your own messages");

        msg.Content = newContent;
        msg.IsEdited = true;
        msg.EditedAt = DateTime.UtcNow;

        return await _groupRepository.UpdateGroupMessageAsync(msg);
    }

    public async Task<bool> DeleteGroupMessageAsync(ulong messageId, ulong userId, ulong groupId)
    {
        var msg = await _groupRepository.GetGroupMessageAsync(messageId);
        if (msg == null) return false;

        if (msg.FromUserId == userId)
        {
            msg.IsDeleted = true;
            msg.DeletedAt = DateTime.UtcNow;
            msg.Content = string.Empty;
            await _groupRepository.UpdateGroupMessageAsync(msg);
            return true;
        }

        var member = await _groupRepository.GetGroupMemberAsync(groupId, userId);
        if (member != null && (member.Role == GroupMemberRole.Owner || member.CanDeleteOthersMessages))
        {
            msg.IsDeleted = true;
            msg.DeletedAt = DateTime.UtcNow;
            msg.Content = string.Empty;
            await _groupRepository.UpdateGroupMessageAsync(msg);
            return true;
        }

        return false;
    }
}
