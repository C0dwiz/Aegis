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

        _registrationService = new UserRegistrationService(_userRepository, _mockCryptoProvider.Object);
        _authService = new UserAuthenticationService(_userRepository, _sessionRepository, _mockCryptoProvider.Object);
        _searchService = new UserSearchService(_userRepository);
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
        var session = await _authService.AuthenticateUserAsync("testuser", "password123", clientInfo);

        // Assert
        Assert.NotNull(session);
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
        var session = await _authService.AuthenticateUserAsync("testuser", "wrongpassword", "Test Client");

        // Assert
        Assert.Null(session);
    }

    [Fact]
    public async Task UserAuthenticationService_AuthenticateUserAsync_ShouldReturnNullOnNonExistentUser()
    {
        // Act
        var session = await _authService.AuthenticateUserAsync("nonexistent", "password123", "Test Client");

        // Assert
        Assert.Null(session);
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

    public void Dispose()
    {
        _context.Dispose();
    }
}
