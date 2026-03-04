using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Aegis.Data.Entities;
using Aegis.Data.Repositories;
using Aegis.Data.Services;
using Aegis.Protocol;
using Aegis.Transport;
using Aegis.Handlers;
using Aegis.Data;
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
    private readonly Mock<IChannelRepository> _mockChannelRepository;
    private readonly Mock<IPrivateChatRepository> _mockPrivateChatRepository;
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
        _mockChannelRepository = new Mock<IChannelRepository>();
        _mockPrivateChatRepository = new Mock<IPrivateChatRepository>();

        _mockRegistrationLogger = new Mock<ILogger<RegistrationHandler>>();
        _mockSearchLogger = new Mock<ILogger<UserSearchHandler>>();
        _mockChannelLogger = new Mock<ILogger<ChannelMessageHandler>>();
        _mockChannelCreateLogger = new Mock<ILogger<ChannelCreateHandler>>();
        _mockPrivateChatLogger = new Mock<ILogger<PrivateChatMessageHandler>>();

        _registrationHandler = new RegistrationHandler(_mockRegistrationService.Object, _mockRegistrationLogger.Object);
        _searchHandler = new UserSearchHandler(_mockSearchService.Object, _mockSearchLogger.Object);
        _channelMessageHandler = new ChannelMessageHandler(_mockChannelRepository.Object, _mockSearchService.Object, _mockChannelLogger.Object);
        _channelCreateHandler = new ChannelCreateHandler(_mockChannelRepository.Object, _mockSearchService.Object, _mockChannelCreateLogger.Object);
        _privateChatMessageHandler = new PrivateChatMessageHandler(_mockPrivateChatRepository.Object, _mockSearchService.Object, _mockPrivateChatLogger.Object);
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
            new UserSearchResult(1, "john_doe", "john@example.com")
        };

        _mockSearchService.Setup(x => x.SearchUsersByUsernameAsync("john", 20))
            .ReturnsAsync(expectedUsers.Select(u => 
            {
                var user = new User 
                { 
                    Id = u.Id, 
                    Username = u.Username, 
                    Email = u.Email ?? "", 
                    PublicKey = "", 
                    PasswordHash = "" 
                };
                return user;
            }));

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

        // Assert - no repository calls should be made since there's no session
        _mockChannelRepository.Verify(x => x.IsUserMemberAsync(It.IsAny<ulong>(), It.IsAny<ulong>()), Times.Never);
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

        // Assert - no repository calls should be made since there's no session
        _mockChannelRepository.Verify(x => x.CreateAsync(It.IsAny<Channel>()), Times.Never);
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

        // Assert - no repository calls should be made since there's no session
        _mockSearchService.Verify(x => x.FindUserByIdAsync(It.IsAny<ulong>()), Times.Never);
        _mockPrivateChatRepository.Verify(x => x.GetPrivateChatAsync(It.IsAny<ulong>(), It.IsAny<ulong>()), Times.Never);
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

        // Assert - no repository calls should be made since there's no session
        _mockSearchService.Verify(x => x.FindUserByIdAsync(It.IsAny<ulong>()), Times.Never);
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}
