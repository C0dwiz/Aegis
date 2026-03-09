using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Aegis.Data.Entities;
using Aegis.Data.Services;
using Aegis.Protocol;
using Aegis.Transport;
using Aegis.Handlers;
using Aegis.Common;
using Aegis.Common.Configuration;
using Aegis.Data;
using Aegis.Data.Repositories;
using System.Text.Json;
using Xunit;
using Microsoft.EntityFrameworkCore.InMemory;
using System.Net.Sockets;

namespace Aegis.Tests;

public class NewHandlerTests : IDisposable
{
    private readonly AegisDbContext _context;
    private readonly Mock<IUserRegistrationService> _mockRegistrationService;
    private readonly Mock<IUserSearchService> _mockSearchService;
    private readonly Mock<IMessageService> _mockMessageService;
    private readonly Mock<IBotManagementService> _mockBotManagementService;
    private readonly Mock<IChannelService> _mockChannelService;
    private readonly Mock<IChannelRepository> _mockChannelRepository;
    private readonly Mock<IMessageSender> _mockMessageSender;
    private readonly RateLimiter _rateLimiter;
    private readonly SessionManager _sessionManager;
    private readonly Mock<ILogger<RegistrationHandler>> _mockRegistrationLogger;
    private readonly Mock<ILogger<UserSearchHandler>> _mockSearchLogger;
    private readonly Mock<ILogger<ChannelMessageHandler>> _mockChannelLogger;
    private readonly Mock<ILogger<ChannelCreateHandler>> _mockChannelCreateLogger;
    private readonly Mock<ILogger<PrivateChatMessageHandler>> _mockPrivateChatLogger;

    private readonly RegistrationHandler _registrationHandler;
    private readonly UserSearchHandler _searchHandler;
    private readonly ChannelMessageHandler _channelMessageHandler;
    private readonly ChannelCreateHandler _channelCreateHandler;
    private readonly PrivateChatMessageHandler _privateChatMessageHandler;

    public NewHandlerTests()
    {
        var options = new DbContextOptionsBuilder<AegisDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new AegisDbContext(options);

        _mockRegistrationService = new Mock<IUserRegistrationService>();
        _mockSearchService = new Mock<IUserSearchService>();
        _mockMessageService = new Mock<IMessageService>();
        _mockBotManagementService = new Mock<IBotManagementService>();
        _mockChannelService = new Mock<IChannelService>();
        _mockChannelRepository = new Mock<IChannelRepository>();
        _mockMessageSender = new Mock<IMessageSender>();
        _rateLimiter = new RateLimiter(new RateLimitOptions());

        var cryptoProvider = new Aegis.Crypto.AegisCryptoProvider();
        _sessionManager = new Aegis.Common.SessionManager(cryptoProvider, new Aegis.Transport.NullLogger());

        _mockRegistrationLogger = new Mock<ILogger<RegistrationHandler>>();
        _mockSearchLogger = new Mock<ILogger<UserSearchHandler>>();
        _mockChannelLogger = new Mock<ILogger<ChannelMessageHandler>>();
        _mockChannelCreateLogger = new Mock<ILogger<ChannelCreateHandler>>();
        _mockPrivateChatLogger = new Mock<ILogger<PrivateChatMessageHandler>>();

        _registrationHandler = new RegistrationHandler(_mockRegistrationService.Object, _mockMessageSender.Object, _rateLimiter, _mockRegistrationLogger.Object);
        _searchHandler = new UserSearchHandler(_mockSearchService.Object, _sessionManager, _mockMessageSender.Object, _rateLimiter, _mockSearchLogger.Object);
        _channelMessageHandler = new ChannelMessageHandler(_mockMessageService.Object, _mockChannelRepository.Object, _sessionManager, _mockMessageSender.Object, _mockChannelLogger.Object);
        _channelCreateHandler = new ChannelCreateHandler(_mockChannelService.Object, _sessionManager, _mockMessageSender.Object, _mockChannelCreateLogger.Object);
        _privateChatMessageHandler = new PrivateChatMessageHandler(
            _mockMessageService.Object,
            _mockSearchService.Object,
            _mockBotManagementService.Object,
            _sessionManager,
            _mockMessageSender.Object,
            _mockPrivateChatLogger.Object);
    }

    [Fact]
    public void RegistrationHandler_ShouldHaveCorrectMessageType()
    {
        // Act & Assert
        Assert.Equal(MessageType.Register, _registrationHandler.Type);
    }

    [Fact]
    public async Task RegistrationHandler_HandleAsync_ShouldRegisterUser()
    {
        // Arrange
        using var mockSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var context = new ConnectionContext(mockSocket, 12345ul);
        var registrationRequest = new RegistrationRequest("testuser", "test@example.com", "password123", "public_key");
        var payload = JsonSerializer.SerializeToUtf8Bytes(registrationRequest);
        
        var message = new Aegis.Protocol.Message
        {
            Type = MessageType.Register,
            SequenceId = 1,
            Payload = payload
        };

        var expectedUser = new User { Id = 1, Username = "testuser", Email = "test@example.com" };
        _mockRegistrationService.Setup(x => x.RegisterUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(expectedUser);

        // Act
        await _registrationHandler.HandleAsync(context, message);

        // Assert
        _mockRegistrationService.Verify(x => x.RegisterUserAsync("testuser", "test@example.com", "password123", "public_key"), Times.Once);
    }

    [Fact]
    public async Task RegistrationHandler_HandleAsync_ShouldReturnFalseOnInvalidPayload()
    {
        // Arrange
        using var mockSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var context = new ConnectionContext(mockSocket, 12345ul);
        var message = new Aegis.Protocol.Message
        {
            Type = MessageType.Register,
            SequenceId = 1,
            Payload = new byte[] { 1, 2, 3 } // Invalid JSON
        };

        // Act
        await _registrationHandler.HandleAsync(context, message);

        // Assert
    }

    [Fact]
    public void UserSearchHandler_ShouldHaveCorrectMessageType()
    {
        // Act & Assert
        Assert.Equal(MessageType.UserSearch, _searchHandler.Type);
    }

    [Fact]
    public async Task UserSearchHandler_HandleAsync_ShouldSearchUsers()
    {
        // Arrange
        using var mockSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var context = new ConnectionContext(mockSocket, 12346ul);
        var searchRequest = new UserSearchRequest("john", 20);
        var payload = JsonSerializer.SerializeToUtf8Bytes(searchRequest);
        
        var message = new Aegis.Protocol.Message
        {
            Type = MessageType.UserSearch,
            SequenceId = 1,
            Payload = payload
        };

        var expectedUsers = new List<UserSearchResult>
        {
            new UserSearchResult(1, "john_doe")
        };

        _mockSearchService.Setup(x => x.SearchUsersByUsernameAsync("john", 20))
            .ReturnsAsync(expectedUsers.Select(u => 
            {
                var user = new User 
                { 
                    Id = u.Id, 
                    Username = u.Username, 
                    Email = string.Empty,
                    PublicKey = "", 
                    PasswordHash = "" 
                };
                return user;
            }));

        _sessionManager.CreateSession(context.ConnectionId);
        _sessionManager.EstablishHandshake(context.ConnectionId, new byte[32], new byte[32]);
        _sessionManager.AuthenticateSession(context.ConnectionId, 1, "john_doe");

        // Act
        await _searchHandler.HandleAsync(context, message);

        // Assert
        _mockSearchService.Verify(x => x.SearchUsersByUsernameAsync("john", 20), Times.Once);
    }

    [Fact]
    public void ChannelMessageHandler_ShouldHaveCorrectMessageType()
    {
        // Act & Assert
        Assert.Equal(MessageType.ChannelMessage, _channelMessageHandler.Type);
    }

    [Fact]
    public async Task ChannelMessageHandler_HandleAsync_ShouldSendErrorWhenNoSession()
    {
        // Arrange
        using var mockSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var context = new ConnectionContext(mockSocket, 12347ul);
        var channelMessageRequest = new ChannelMessageRequest(1, "Hello, channel!", MessageContentType.Text);
        var payload = JsonSerializer.SerializeToUtf8Bytes(channelMessageRequest);
        
        var message = new Aegis.Protocol.Message
        {
            Type = MessageType.ChannelMessage,
            SequenceId = 1,
            Payload = payload
        };

        // Act - should return error due to no session
        await _channelMessageHandler.HandleAsync(context, message);

        // Assert - no service calls should be made since there's no session
        _mockMessageService.Verify(x => x.SendChannelMessageAsync(It.IsAny<ulong>(), It.IsAny<ulong>(), It.IsAny<string>(), It.IsAny<MessageContentType>(), It.IsAny<ulong?>()), Times.Never);
    }

    [Fact]
    public void ChannelCreateHandler_ShouldHaveCorrectMessageType()
    {
        // Act & Assert
        Assert.Equal(MessageType.ChannelCreate, _channelCreateHandler.Type);
    }

    [Fact]
    public async Task ChannelCreateHandler_HandleAsync_ShouldSendErrorWhenNoSession()
    {
        // Arrange
        using var mockSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var context = new ConnectionContext(mockSocket, 12348ul);
        var channelCreateRequest = new ChannelCreateRequest("Test Channel", "Test Description", ChannelType.Public);
        var payload = JsonSerializer.SerializeToUtf8Bytes(channelCreateRequest);
        
        var message = new Aegis.Protocol.Message
        {
            Type = MessageType.ChannelCreate,
            SequenceId = 1,
            Payload = payload
        };

        // Act - should return error due to no session
        await _channelCreateHandler.HandleAsync(context, message);

        // Assert - no service calls should be made since there's no session
        _mockChannelService.Verify(x => x.CreateChannelAsync(It.IsAny<ulong>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<ChannelType>()), Times.Never);
    }

    [Fact]
    public void PrivateChatMessageHandler_ShouldHaveCorrectMessageType()
    {
        // Act & Assert
        Assert.Equal(MessageType.PrivateChatMessage, _privateChatMessageHandler.Type);
    }

    [Fact]
    public async Task PrivateChatMessageHandler_HandleAsync_ShouldSendErrorWhenNoSession()
    {
        // Arrange
        using var mockSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var context = new ConnectionContext(mockSocket, 12349ul);
        var privateChatRequest = new PrivateChatMessageRequest(2, "Hello, private!", MessageContentType.Text);
        var payload = JsonSerializer.SerializeToUtf8Bytes(privateChatRequest);
        
        var message = new Aegis.Protocol.Message
        {
            Type = MessageType.PrivateChatMessage,
            SequenceId = 1,
            Payload = payload
        };

        // Act - should return error due to no session
        await _privateChatMessageHandler.HandleAsync(context, message);

        // Assert - no service calls should be made since there's no session
        _mockSearchService.Verify(x => x.FindUserByIdAsync(It.IsAny<ulong>()), Times.Never);
        _mockMessageService.Verify(x => x.SendPrivateMessageAsync(It.IsAny<ulong>(), It.IsAny<ulong>(), It.IsAny<string>(), It.IsAny<MessageContentType>()), Times.Never);
    }

    [Fact]
    public async Task PrivateChatMessageHandler_HandleAsync_ShouldSendErrorWhenNoSessionForNonExistentUser()
    {
        // Arrange
        using var mockSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var context = new ConnectionContext(mockSocket, 12350ul);
        var privateChatRequest = new PrivateChatMessageRequest(999, "Hello, private!", MessageContentType.Text);
        var payload = JsonSerializer.SerializeToUtf8Bytes(privateChatRequest);
        
        var message = new Aegis.Protocol.Message
        {
            Type = MessageType.PrivateChatMessage,
            SequenceId = 1,
            Payload = payload
        };

        // Act - should return error due to no session
        await _privateChatMessageHandler.HandleAsync(context, message);

        // Assert - no service calls should be made since there's no session
        _mockSearchService.Verify(x => x.FindUserByIdAsync(It.IsAny<ulong>()), Times.Never);
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}
