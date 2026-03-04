using Aegis.Data.Entities;
using Aegis.Data.Repositories;
using Aegis.Common;

namespace Aegis.Data.Services;

/// <summary>
/// User registration service interface
/// </summary>
public interface IUserRegistrationService
{
    Task<User> RegisterUserAsync(string username, string email, string password, string publicKey);
    Task<bool> IsUsernameAvailableAsync(string username);
    Task<bool> IsEmailAvailableAsync(string email);
}

/// <summary>
/// User registration service implementation
/// </summary>
public class UserRegistrationService : IUserRegistrationService
{
    private readonly IUserRepository _userRepository;
    private readonly ICryptoProvider _cryptoProvider;

    public UserRegistrationService(IUserRepository userRepository, ICryptoProvider cryptoProvider)
    {
        _userRepository = userRepository;
        _cryptoProvider = cryptoProvider;
    }

    public async Task<User> RegisterUserAsync(string username, string email, string password, string publicKey)
    {
        // Validate input
        if (string.IsNullOrWhiteSpace(username) || username.Length < 3)
            throw new ArgumentException("Username must be at least 3 characters long", nameof(username));
        
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            throw new ArgumentException("Invalid email format", nameof(email));
        
        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
            throw new ArgumentException("Password must be at least 6 characters long", nameof(password));
        
        if (string.IsNullOrWhiteSpace(publicKey))
            throw new ArgumentException("Public key is required", nameof(publicKey));

        // Check availability
        if (!await IsUsernameAvailableAsync(username))
            throw new InvalidOperationException("Username is already taken");
        
        if (!await IsEmailAvailableAsync(email))
            throw new InvalidOperationException("Email is already registered");

        // Hash password
        var passwordHash = await _cryptoProvider.HashPasswordAsync(password);

        // Create user
        var user = new User
        {
            Username = username,
            Email = email,
            PasswordHash = passwordHash,
            PublicKey = publicKey,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        return await _userRepository.CreateAsync(user);
    }

    public async Task<bool> IsUsernameAvailableAsync(string username)
    {
        var existingUser = await _userRepository.GetByUsernameAsync(username);
        return existingUser == null;
    }

    public async Task<bool> IsEmailAvailableAsync(string email)
    {
        var existingUser = await _userRepository.GetByEmailAsync(email);
        return existingUser == null;
    }
}

/// <summary>
/// User authentication service interface
/// </summary>
public interface IUserAuthenticationService
{
    Task<Session?> AuthenticateUserAsync(string username, string password, string clientInfo, string? ipAddress = null);
    Task<Session?> AuthenticateUserByTokenAsync(string token);
    Task<bool> ValidateSessionAsync(string token);
    Task<bool> LogoutAsync(string token);
}

/// <summary>
/// User authentication service implementation
/// </summary>
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

    public async Task<Session?> AuthenticateUserAsync(string username, string password, string clientInfo, string? ipAddress = null)
    {
        // Find user by username
        var user = await _userRepository.GetByUsernameAsync(username);
        if (user == null || !user.IsActive)
            return null;

        // Verify password
        var isValidPassword = await _cryptoProvider.VerifyPasswordAsync(password, user.PasswordHash);
        if (!isValidPassword)
            return null;

        // Generate session token and keys
        var sessionToken = GenerateSessionToken();
        var sessionKey = await _cryptoProvider.GenerateSessionKeyAsync();
        var sessionKeyHash = await _cryptoProvider.HashAsync(Convert.ToBase64String(sessionKey));

        // Create session
        var session = new Session
        {
            UserId = user.Id,
            SessionToken = sessionToken,
            SessionKeyHash = sessionKeyHash,
            ClientInfo = clientInfo,
            IpAddress = ipAddress,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(30), // 30 days expiry
            LastActivityAt = DateTime.UtcNow,
            IsActive = true
        };

        return await _sessionRepository.CreateAsync(session);
    }

    public async Task<Session?> AuthenticateUserByTokenAsync(string token)
    {
        var session = await _sessionRepository.GetByTokenAsync(token);
        if (session == null || !session.IsActive || session.ExpiresAt < DateTime.UtcNow)
            return null;

        // Update last activity
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

/// <summary>
/// User search service interface
/// </summary>
public interface IUserSearchService
{
    Task<User?> FindUserByUsernameAsync(string username);
    Task<IEnumerable<User>> SearchUsersByUsernameAsync(string pattern, int limit = 20);
    Task<User?> FindUserByIdAsync(ulong userId);
}

/// <summary>
/// User search service implementation
/// </summary>
public class UserSearchService : IUserSearchService
{
    private readonly IUserRepository _userRepository;

    public UserSearchService(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<User?> FindUserByUsernameAsync(string username)
    {
        return await _userRepository.GetByUsernameAsync(username);
    }

    public async Task<IEnumerable<User>> SearchUsersByUsernameAsync(string pattern, int limit = 20)
    {
        var users = await _userRepository.SearchByUsernameAsync(pattern);
        return users.Take(limit);
    }

    public async Task<User?> FindUserByIdAsync(ulong userId)
    {
        return await _userRepository.GetByIdAsync(userId);
    }
}
