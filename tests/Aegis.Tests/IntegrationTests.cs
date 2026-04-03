using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Aegis.Data;
using Aegis.Data.Entities;
using Aegis.Data.Repositories;
using Aegis.Data.Services;
using Aegis.Protocol;
using Aegis.Transport;
using Aegis.Handlers;
using Aegis.Common;
using Aegis.Common.Configuration;
using System.Text.Json;
using Xunit;
using Microsoft.EntityFrameworkCore.InMemory;
using System.Net.Sockets;
using Moq;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text;

namespace Aegis.Tests;

public class IntegrationTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly AegisDbContext _context;

    public IntegrationTests()
    {
        var services = new ServiceCollection();

        // Configure in-memory database
        services.AddDbContext<AegisDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));

        // Add repositories
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IChannelRepository, ChannelRepository>();
        services.AddScoped<IPrivateChatRepository, PrivateChatRepository>();

        // Add services
        services.AddScoped<Aegis.Data.Utils.FastIdGenerator>(_ => new Aegis.Data.Utils.FastIdGenerator(1));
        services.AddScoped<IUserRegistrationService, UserRegistrationService>();
        services.AddScoped<IUserAuthenticationService, UserAuthenticationService>();
        services.AddScoped<IUserTwoFactorService, UserTwoFactorService>();
        services.AddScoped<IUserSearchService, UserSearchService>();

        // Add logging
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug));

        // Add crypto provider mock
        var mockCryptoProvider = new Moq.Mock<Aegis.Common.ICryptoProvider>();
        mockCryptoProvider.Setup(x => x.HashPasswordAsync(Moq.It.IsAny<string>()))
            .ReturnsAsync((string password) => $"hash:{password}");
        mockCryptoProvider.Setup(x => x.VerifyPasswordAsync(Moq.It.IsAny<string>(), Moq.It.IsAny<string>()))
            .ReturnsAsync((string password, string hash) => hash == $"hash:{password}");
        mockCryptoProvider.Setup(x => x.GenerateSessionKeyAsync())
            .ReturnsAsync(new byte[] { 1, 2, 3, 4 });
        mockCryptoProvider.Setup(x => x.HashAsync(Moq.It.IsAny<string>()))
            .ReturnsAsync("hashed_session_key");

        services.AddSingleton(mockCryptoProvider.Object);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:TotpEncryptionKey"] = Convert.ToBase64String(Enumerable.Repeat((byte)11, 32).ToArray())
            })
            .Build();

        services.AddSingleton<IConfiguration>(configuration);

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<AegisDbContext>();
    }

    [Fact]
    public async Task CompleteUserFlow_ShouldWork()
    {
        // Arrange
        var registrationService = _serviceProvider.GetRequiredService<IUserRegistrationService>();
        var authService = _serviceProvider.GetRequiredService<IUserAuthenticationService>();
        var searchService = _serviceProvider.GetRequiredService<IUserSearchService>();

        // Act & Assert - Registration
        var user = await registrationService.RegisterUserAsync(
            "testuser",
            "test@example.com",
            "password123",
            "public_key");

        Assert.NotNull(user);
        Assert.Equal("testuser", user.Username);

        // Act & Assert - Authentication
        var authResult = await authService.AuthenticateUserAsync(
            "testuser",
            "password123",
            "Test Client",
            "127.0.0.1");

        Assert.NotNull(authResult);
        Assert.Equal(user.Id, authResult.Value.Session.UserId);

        // Act & Assert - User Search
        var foundUser = await searchService.FindUserByUsernameAsync("testuser");
        Assert.NotNull(foundUser);
        Assert.Equal(user.Id, foundUser.Id);

        // Act & Assert - Search by pattern
        var searchResults = await searchService.SearchUsersByUsernameAsync("test");
        Assert.NotEmpty(searchResults);
        Assert.Contains(searchResults, u => u.Username == "testuser");
    }

    [Fact]
    public async Task ChannelFlow_ShouldWork()
    {
        // Arrange
        var registrationService = _serviceProvider.GetRequiredService<IUserRegistrationService>();
        var channelRepository = _serviceProvider.GetRequiredService<IChannelRepository>();

        var user = await registrationService.RegisterUserAsync(
            "channeluser",
            "channel@example.com",
            "password123",
            "public_key");

        // Act & Assert - Create Channel
        var channel = new Channel
        {
            Name = "Test Channel",
            Description = "Test Description",
            Type = ChannelType.Public,
            CreatedByUserId = user.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsActive = true,
            MemberCount = 1
        };

        var createdChannel = await channelRepository.CreateAsync(channel);
        Assert.NotNull(createdChannel);
        Assert.Equal("Test Channel", createdChannel.Name);

        // Act & Assert - Add Channel Member
        var channelMember = new ChannelMember
        {
            ChannelId = createdChannel.Id,
            UserId = user.Id,
            Role = ChannelMemberRole.Owner,
            JoinedAt = DateTime.UtcNow,
            IsActive = true
        };

        _context.ChannelMembers.Add(channelMember);
        await _context.SaveChangesAsync();

        // Verify user is member
        var isMember = await channelRepository.IsUserMemberAsync(createdChannel.Id, user.Id);
        Assert.True(isMember);

        // Get user channels
        var userChannels = await channelRepository.GetUserChannelsAsync(user.Id);
        Assert.Single(userChannels);
        Assert.Equal(createdChannel.Id, userChannels.First().Id);
    }

    [Fact]
    public async Task PrivateChatFlow_ShouldWork()
    {
        // Arrange
        var registrationService = _serviceProvider.GetRequiredService<IUserRegistrationService>();
        var privateChatRepository = _serviceProvider.GetRequiredService<IPrivateChatRepository>();

        var user1 = await registrationService.RegisterUserAsync(
            "user1",
            "user1@example.com",
            "password123",
            "public_key1");

        var user2 = await registrationService.RegisterUserAsync(
            "user2",
            "user2@example.com",
            "password123",
            "public_key2");

        // Act & Assert - Create Private Chat
        var privateChat = await privateChatRepository.CreatePrivateChatAsync(user1.Id, user2.Id);
        Assert.NotNull(privateChat);
        Assert.Equal(Math.Min(user1.Id, user2.Id), privateChat.User1Id);
        Assert.Equal(Math.Max(user1.Id, user2.Id), privateChat.User2Id);

        // Get private chat
        var retrievedChat = await privateChatRepository.GetPrivateChatAsync(user1.Id, user2.Id);
        Assert.NotNull(retrievedChat);
        Assert.Equal(privateChat.Id, retrievedChat.Id);

        // Get user private chats
        var user1Chats = await privateChatRepository.GetUserPrivateChatsAsync(user1.Id);
        Assert.Single(user1Chats);

        var user2Chats = await privateChatRepository.GetUserPrivateChatsAsync(user2.Id);
        Assert.Single(user2Chats);
    }

    [Fact]
    public async Task MessageHandlers_ShouldProcessCorrectly()
    {
        // Arrange
        var registrationService = _serviceProvider.GetRequiredService<IUserRegistrationService>();
        var searchService = _serviceProvider.GetRequiredService<IUserSearchService>();
        var channelRepository = _serviceProvider.GetRequiredService<IChannelRepository>();

        var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
        var messageSender = new Mock<IMessageSender>().Object;
        var rateLimiter = new RateLimiter(new RateLimitOptions());
        var sessionManager = new SessionManager(new Aegis.Crypto.AegisCryptoProvider(), new NullLogger());

        var user = await registrationService.RegisterUserAsync(
            "handleruser",
            "handler@example.com",
            "password123",
            "public_key");

        // Test Registration Handler
        var registrationHandler = new RegistrationHandler(
            registrationService,
            messageSender,
            rateLimiter,
            loggerFactory.CreateLogger<RegistrationHandler>());
        Assert.Equal(MessageType.Register, registrationHandler.Type);

        var registrationRequest = new RegistrationRequest("newuser", "new@example.com", "password123", "new_public_key");
        var registrationPayload = JsonSerializer.SerializeToUtf8Bytes(registrationRequest);
        var registrationMessage = new Aegis.Protocol.Message
        {
            Type = MessageType.Register,
            SequenceId = 1,
            Payload = registrationPayload
        };

        using var mockSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var context = new ConnectionContext(mockSocket, 12345ul);
        await registrationHandler.HandleAsync(context, registrationMessage);

        // Test User Search Handler
        sessionManager.CreateSession(context.ConnectionId);
        sessionManager.EstablishHandshake(context.ConnectionId, new byte[32]);
        sessionManager.AuthenticateSession(context.ConnectionId, user.Id, user.Username);

        var searchHandler = new UserSearchHandler(
            searchService,
            sessionManager,
            messageSender,
            rateLimiter,
            loggerFactory.CreateLogger<UserSearchHandler>());
        Assert.Equal(MessageType.UserSearch, searchHandler.Type);

        var searchRequest = new UserSearchRequest("handler", 10);
        var searchPayload = JsonSerializer.SerializeToUtf8Bytes(searchRequest);
        var searchMessage = new Aegis.Protocol.Message
        {
            Type = MessageType.UserSearch,
            SequenceId = 2,
            Payload = searchPayload
        };

        await searchHandler.HandleAsync(context, searchMessage);
    }

    [Fact]
    public async Task DatabaseConstraints_ShouldBeEnforced()
    {
        // Arrange
        var userRepository = _serviceProvider.GetRequiredService<IUserRepository>();
        var registrationService = _serviceProvider.GetRequiredService<IUserRegistrationService>();

        // Act & Assert - Unique Username Constraint
        await registrationService.RegisterUserAsync("uniqueuser", "unique1@example.com", "password123", "public_key1");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => registrationService.RegisterUserAsync("uniqueuser", "unique2@example.com", "password123", "public_key2"));

        // Act & Assert - Unique Email Constraint
        await registrationService.RegisterUserAsync("anotheruser", "unique@example.com", "password123", "public_key3");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => registrationService.RegisterUserAsync("yetanotheruser", "unique@example.com", "password123", "public_key4"));
    }

    [Fact]
    public async Task SessionManagement_ShouldWork()
    {
        // Arrange
        var registrationService = _serviceProvider.GetRequiredService<IUserRegistrationService>();
        var authService = _serviceProvider.GetRequiredService<IUserAuthenticationService>();
        var sessionRepository = _serviceProvider.GetRequiredService<ISessionRepository>();

        var user = await registrationService.RegisterUserAsync(
            "sessionuser",
            "session@example.com",
            "password123",
            "public_key");

        // Act & Assert - Create Session
        var result1 = await authService.AuthenticateUserAsync(
            "sessionuser",
            "password123",
            "Client 1",
            "127.0.0.1");

        var result2 = await authService.AuthenticateUserAsync(
            "sessionuser",
            "password123",
            "Client 2",
            "127.0.0.2");

        Assert.NotNull(result1);
        Assert.NotNull(result2);
        var session1 = result1.Value.Session;
        var session2 = result2.Value.Session;
        Assert.NotEqual(session1.SessionToken, session2.SessionToken);

        // Get user sessions
        var userSessions = await sessionRepository.GetUserActiveSessions(user.Id);
        Assert.Equal(2, userSessions.Count());

        // Logout
        var logoutResult = await authService.LogoutAsync(session1.SessionToken);
        Assert.True(logoutResult);

        var validateResult = await authService.ValidateSessionAsync(session1.SessionToken);
        Assert.False(validateResult);
    }

    [Fact]
    public async Task AuthHardeningFlow_With2FaAndRecovery_ShouldWorkEndToEnd()
    {
        var registrationService = _serviceProvider.GetRequiredService<IUserRegistrationService>();
        var authService = _serviceProvider.GetRequiredService<IUserAuthenticationService>();
        var twoFactorService = _serviceProvider.GetRequiredService<IUserTwoFactorService>();
        var userRepository = _serviceProvider.GetRequiredService<IUserRepository>();

        var user = await registrationService.RegisterUserAsync(
            "hardflow",
            "hardflow@example.com",
            "password123",
            "public_key",
            isEmailVerified: false);

        var beforeVerify = await authService.AuthenticateUserWithStatusAsync("hardflow", "password123", "itest", "127.0.0.1");
        Assert.False(beforeVerify.Success);
        Assert.Equal(AuthFailureReason.EmailNotVerified, beforeVerify.FailureReason);

        user.IsEmailVerified = true;
        user.EmailVerifiedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;
        await userRepository.UpdateAsync(user);

        var setup = await twoFactorService.BeginSetupAsync(user.Id, "Twospace");
        var code = ComputeCurrentTotp(setup.Secret);
        var enabled = await twoFactorService.EnableAsync(user.Id, code);
        Assert.True(enabled);

        var no2fa = await authService.AuthenticateUserWithStatusAsync("hardflow", "password123", "itest", "127.0.0.1");
        Assert.False(no2fa.Success);
        Assert.Equal(AuthFailureReason.TwoFactorRequired, no2fa.FailureReason);

        var wrong2fa = await authService.AuthenticateUserWithStatusAsync("hardflow", "password123", "itest", "127.0.0.1", twoFactorCode: "000000");
        Assert.False(wrong2fa.Success);
        Assert.Equal(AuthFailureReason.TwoFactorInvalid, wrong2fa.FailureReason);

        var ok2fa = await authService.AuthenticateUserWithStatusAsync("hardflow", "password123", "itest", "127.0.0.1", twoFactorCode: ComputeCurrentTotp(setup.Secret));
        Assert.True(ok2fa.Success);

        var byRecovery = await authService.AuthenticateUserWithStatusAsync("hardflow", "password123", "itest", "127.0.0.1", recoveryPhrase: setup.RecoveryPhrase);
        Assert.True(byRecovery.Success);

        var refreshedUser = await userRepository.GetByIdAsync(user.Id);
        Assert.NotNull(refreshedUser);
        Assert.False(refreshedUser!.TwoFactorEnabled);
        Assert.Null(refreshedUser.TotpSecret);
        Assert.Null(refreshedUser.RecoveryPhraseHash);

        var setPassword = await authService.SetPasswordAsync(user.Id, "newpassword123");
        Assert.True(setPassword);

        var oldPassword = await authService.AuthenticateUserWithStatusAsync("hardflow", "password123", "itest", "127.0.0.1");
        Assert.False(oldPassword.Success);

        var newPassword = await authService.AuthenticateUserWithStatusAsync("hardflow", "newpassword123", "itest", "127.0.0.1");
        Assert.True(newPassword.Success);
    }

    private static string ComputeCurrentTotp(string base32Secret)
    {
        var secret = DecodeBase32(base32Secret);
        var counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;

        Span<byte> counterBytes = stackalloc byte[8];
        var tmp = counter;
        for (var i = 7; i >= 0; i--)
        {
            counterBytes[i] = (byte)(tmp & 0xff);
            tmp >>= 8;
        }

        using var hmac = new HMACSHA1(secret);
        var hash = hmac.ComputeHash(counterBytes.ToArray());
        var offset = hash[^1] & 0x0f;
        var binaryCode = ((hash[offset] & 0x7f) << 24)
                         | ((hash[offset + 1] & 0xff) << 16)
                         | ((hash[offset + 2] & 0xff) << 8)
                         | (hash[offset + 3] & 0xff);
        return (binaryCode % 1_000_000).ToString("D6");
    }

    private static byte[] DecodeBase32(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var cleaned = (input ?? string.Empty).Trim().TrimEnd('=').ToUpperInvariant();
        var result = new List<byte>((cleaned.Length * 5) / 8);

        var buffer = 0;
        var bitsLeft = 0;

        foreach (var ch in cleaned)
        {
            var val = alphabet.IndexOf(ch);
            if (val < 0)
            {
                throw new ArgumentException("Invalid Base32 input", nameof(input));
            }

            buffer = (buffer << 5) | val;
            bitsLeft += 5;

            if (bitsLeft >= 8)
            {
                bitsLeft -= 8;
                result.Add((byte)((buffer >> bitsLeft) & 0xff));
            }
        }

        return result.ToArray();
    }

    public void Dispose()
    {
        _context.Dispose();
        _serviceProvider.Dispose();
    }
}
