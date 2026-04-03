using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Aegis.Data.Entities;
using Aegis.Data.Repositories;
using Aegis.Data.Services;
using Aegis.Protocol;
using Aegis.Transport;
using Aegis.Handlers;
using Aegis.Crypto;
using Aegis.Data;
using System.Text.Json;
using Xunit;
using Microsoft.EntityFrameworkCore.InMemory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using Aegis.Data.Utils;

namespace Aegis.Tests;

public class UserServicesTests : IDisposable
{
    private readonly AegisDbContext _context;
    private readonly UserRepository _userRepository;
    private readonly SessionRepository _sessionRepository;
    private readonly Mock<Aegis.Common.ICryptoProvider> _mockCryptoProvider;
    private readonly UserRegistrationService _registrationService;
    private readonly UserAuthenticationService _authService;
    private readonly UserSearchService _searchService;
    private readonly UserTwoFactorService _twoFactorService;

    public UserServicesTests()
    {
        var options = new DbContextOptionsBuilder<AegisDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new AegisDbContext(options);
        _userRepository = new UserRepository(_context);
        _sessionRepository = new SessionRepository(_context);

        _mockCryptoProvider = new Mock<Aegis.Common.ICryptoProvider>();
        _mockCryptoProvider.Setup(x => x.HashPasswordAsync(It.IsAny<string>()))
            .ReturnsAsync("hashed_password");
        _mockCryptoProvider.Setup(x => x.VerifyPasswordAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        _mockCryptoProvider.Setup(x => x.GenerateSessionKeyAsync())
            .ReturnsAsync(new byte[] { 1, 2, 3, 4 });
        _mockCryptoProvider.Setup(x => x.HashAsync(It.IsAny<string>()))
            .ReturnsAsync("hashed_session_key");

        var idGenerator = new FastIdGenerator(1);

        _registrationService = new UserRegistrationService(
        _userRepository,
        _mockCryptoProvider.Object,
        NullLogger<UserRegistrationService>.Instance,
        idGenerator);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:TotpEncryptionKey"] = Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray())
            })
            .Build();

        _twoFactorService = new UserTwoFactorService(_userRepository, _mockCryptoProvider.Object, _context, configuration);
        _authService = new UserAuthenticationService(_userRepository, _sessionRepository, _mockCryptoProvider.Object, _twoFactorService);
        _searchService = new UserSearchService(_userRepository, null, NullLogger<UserSearchService>.Instance);
    }
    [Fact]
    public async Task UserRegistrationService_RegisterUserAsync_ShouldCreateUser()
    {
        // Arrange
        var username = "testuser";
        var email = "test@example.com";
        var password = "password123";
        var publicKey = "public_key";

        // Act
        var user = await _registrationService.RegisterUserAsync(username, email, password, publicKey);

        // Assert
        Assert.NotNull(user);
        Assert.Equal(username, user.Username);
        Assert.Equal(email, user.Email);
        Assert.Equal(publicKey, user.PublicKey);
        Assert.Equal("hashed_password", user.PasswordHash);
        Assert.True(user.IsActive);
    }

    [Fact]
    public async Task UserRegistrationService_RegisterUserAsync_ShouldThrowOnDuplicateUsername()
    {
        // Arrange
        var username = "testuser";
        await _registrationService.RegisterUserAsync(username, "test1@example.com", "password123", "public_key");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _registrationService.RegisterUserAsync(username, "test2@example.com", "password123", "public_key"));
    }

    [Fact]
    public async Task UserRegistrationService_RegisterUserAsync_ShouldThrowOnDuplicateEmail()
    {
        // Arrange
        var email = "test@example.com";
        await _registrationService.RegisterUserAsync("user1", email, "password123", "public_key");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _registrationService.RegisterUserAsync("user2", email, "password123", "public_key"));
    }

    [Fact]
    public async Task UserRegistrationService_RegisterUserAsync_ShouldThrowOnInvalidUsername()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _registrationService.RegisterUserAsync("ab", "test@example.com", "password123", "public_key"));
    }

    [Fact]
    public async Task UserRegistrationService_RegisterUserAsync_ShouldThrowOnInvalidEmail()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _registrationService.RegisterUserAsync("testuser", "invalid-email", "password123", "public_key"));
    }

    [Fact]
    public async Task UserRegistrationService_RegisterUserAsync_ShouldThrowOnShortPassword()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _registrationService.RegisterUserAsync("testuser", "test@example.com", "123", "public_key"));
    }

    [Fact]
    public async Task UserAuthenticationService_AuthenticateUserAsync_ShouldCreateSession()
    {
        // Arrange
        var user = await _registrationService.RegisterUserAsync("testuser", "test@example.com", "password123", "public_key");
        var clientInfo = "Test Client";

        // Act
        var result = await _authService.AuthenticateUserAsync("testuser", "password123", clientInfo);

        // Assert
        Assert.NotNull(result);
        var (authUser, session) = result.Value;
        Assert.Equal(user.Id, session.UserId);
        Assert.Equal(clientInfo, session.ClientInfo);
        Assert.Equal("hashed_session_key", session.SessionKeyHash);
        Assert.True(session.IsActive);
        Assert.True(session.ExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task UserAuthenticationService_AuthenticateUserAsync_ShouldReturnNullOnInvalidCredentials()
    {
        // Arrange
        await _registrationService.RegisterUserAsync("testuser", "test@example.com", "password123", "public_key");
        _mockCryptoProvider.Setup(x => x.VerifyPasswordAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        // Act
        var result = await _authService.AuthenticateUserAsync("testuser", "wrongpassword", "Test Client");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task UserAuthenticationService_AuthenticateUserAsync_ShouldReturnNullOnNonExistentUser()
    {
        // Act
        var result = await _authService.AuthenticateUserAsync("nonexistent", "password123", "Test Client");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task UserSearchService_FindUserByUsernameAsync_ShouldReturnUser()
    {
        // Arrange
        var createdUser = await _registrationService.RegisterUserAsync("testuser", "test@example.com", "password123", "public_key");

        // Act
        var foundUser = await _searchService.FindUserByUsernameAsync("testuser");

        // Assert
        Assert.NotNull(foundUser);
        Assert.Equal(createdUser.Id, foundUser.Id);
        Assert.Equal("testuser", foundUser.Username);
    }

    [Fact]
    public async Task UserSearchService_FindUserByUsernameAsync_ShouldReturnNullOnNonExistentUser()
    {
        // Act
        var user = await _searchService.FindUserByUsernameAsync("nonexistent");

        // Assert
        Assert.Null(user);
    }

    [Fact]
    public async Task UserSearchService_SearchUsersByUsernameAsync_ShouldReturnMatchingUsers()
    {
        // Arrange
        await _registrationService.RegisterUserAsync("john_doe", "john@example.com", "password123", "public_key");
        await _registrationService.RegisterUserAsync("jane_doe", "jane@example.com", "password123", "public_key");
        await _registrationService.RegisterUserAsync("bob_smith", "bob@example.com", "password123", "public_key");

        // Act
        var users = await _searchService.SearchUsersByUsernameAsync("john");

        // Assert
        Assert.Single(users);
        Assert.Equal("john_doe", users.First().Username);
    }

    [Fact]
    public async Task UserSearchService_SearchUsersByUsernameAsync_ShouldRespectLimit()
    {
        // Arrange
        for (int i = 0; i < 5; i++)
        {
            await _registrationService.RegisterUserAsync($"user{i}", $"user{i}@example.com", "password123", "public_key");
        }

        // Act
        var users = await _searchService.SearchUsersByUsernameAsync("user", 3);

        // Assert
        Assert.Equal(3, users.Count());
    }

    [Fact]
    public async Task UserTwoFactorService_BeginSetupAsync_ShouldGenerateRecoveryPhraseWith20Words()
    {
        var user = await _registrationService.RegisterUserAsync("totpuser", "totp@example.com", "password123", "public_key");

        var setup = await _twoFactorService.BeginSetupAsync(user.Id, "Twospace");

        Assert.False(string.IsNullOrWhiteSpace(setup.Secret));
        Assert.False(string.IsNullOrWhiteSpace(setup.OtpauthUri));
        Assert.Equal(20, setup.RecoveryPhrase.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}
