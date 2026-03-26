using Xunit;
using System.Net.Sockets;
using System.Threading.Tasks;
using Aegis.Handlers;
using Aegis.Protocol;
using Aegis.Transport;
using Aegis.Common.Logging;
using Aegis.Crypto;
using Aegis.Common;
using Aegis.Data.Services;
using Aegis.Data.Repositories;
using Aegis.Data.Entities;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using System.Text;

namespace Aegis.Tests;

public class TestMessageSender : IMessageSender
{
    public List<ulong> SentConnectionIds { get; } = new();
    public List<byte[]> SentMessages { get; } = new();
    
    public Task SendMessageAsync(ulong connectionId, byte[] encryptedMessage)
    {
        SentConnectionIds.Add(connectionId);
        SentMessages.Add(encryptedMessage);
        return Task.CompletedTask;
    }

    public Task SendProtocolMessageAsync(ulong connectionId, ushort messageType, ulong sequenceId, byte[] payload, bool allowUnsigned = false)
    {
        var message = new Aegis.Protocol.Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = ProtocolConstants.VersionMajor,
            VersionMinor = ProtocolConstants.VersionMinor,
            Type = (MessageType)messageType,
            SequenceId = sequenceId,
            Payload = payload,
            PayloadLength = (uint)payload.Length,
        };

        var buffer = new byte[Aegis.Protocol.Message.TotalSize(message)];
        MessageEncoder.Encode(message, buffer);
        return SendMessageAsync(connectionId, buffer);
    }
}

public class HandlerTests
{
    private readonly TestLogger _logger = new TestLogger();
    private readonly TestAntiSpamClient _antiSpam = new TestAntiSpamClient();
    private readonly TestMessageSender _messageSender = new TestMessageSender();
    private readonly AegisCryptoProvider _cryptoProvider = new AegisCryptoProvider();
    private readonly Mock<IMessageService> _messageServiceMock = new Mock<IMessageService>();
    private readonly SessionManager _sessionManager;
    
    public HandlerTests()
    {
        _sessionManager = new SessionManager(_cryptoProvider, _logger);
        _messageServiceMock
            .Setup(s => s.SendPrivateMessageAsync(It.IsAny<ulong>(), It.IsAny<ulong>(), It.IsAny<string>(), It.IsAny<MessageContentType>()))
            .ReturnsAsync((ulong fromUserId, ulong toUserId, string content, MessageContentType _) => new Aegis.Data.Entities.Message
            {
                Id = 100,
                FromUserId = fromUserId,
                ToUserId = toUserId,
                Content = content,
                CreatedAt = DateTime.UtcNow
            });
    }
    
    [Fact]
    public async Task MessageRouter_RegisterHandler_ShouldRouteCorrectly()
    {
        // Arrange
        var testHandler = new TestMessageHandler();
        var router = MessageRouter.ForHandlers(new IMessageHandler[] { testHandler }, _messageSender, _logger);
        var context = new TestConnectionContext(12345ul);
        var message = new Aegis.Protocol.Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = 1,
            VersionMinor = 0,
            Flags = 0,
            Type = MessageType.Message,
            SequenceId = 1,
            PayloadLength = 5,
            Payload = new byte[] { 1, 2, 3, 4, 5 },
        };
        
        // Act
        await router.RouteAsync(context, message);
        
        // Assert
        Assert.True(testHandler.Handled);
        Assert.Equal(message.SequenceId, testHandler.HandledSequenceId);
        Assert.Equal(context.ConnectionId, testHandler.HandledConnectionId);
    }
    
    [Fact]
    public async Task MessageRouter_UnknownMessage_ShouldSendError()
    {
        // Arrange
        var router = MessageRouter.ForHandlers(Array.Empty<IMessageHandler>(), _messageSender, _logger);
        var context = new TestConnectionContext(12345ul);
        var message = new Aegis.Protocol.Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = 1,
            VersionMinor = 0,
            Flags = 0,
            Type = (MessageType)999,
            SequenceId = 1,
            PayloadLength = 0,
            Payload = Array.Empty<byte>(),
        };
        
        // Act
        await router.RouteAsync(context, message);
        
        // Assert
        Assert.NotEmpty(_logger.ErrorMessages);
        Assert.Contains(_logger.ErrorMessages, m => m.Contains("Unknown message type"));
    }
    
    [Fact]
    public async Task AuthHandler_ShouldProcessAuthMessages()
    {
        // Arrange
        var handler = new AuthHandler(
            new Mock<IUserAuthenticationService>().Object,
            new RateLimiter(new Aegis.Common.Configuration.RateLimitOptions()),
            _sessionManager,
            _messageSender,
            new Mock<IMessageRepository>().Object,
            new Mock<IUserSearchService>().Object,
            new Mock<Microsoft.Extensions.Logging.ILogger<AuthHandler>>().Object);
        var context = new TestConnectionContext(12345ul);
        var message = new Aegis.Protocol.Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = 1,
            VersionMinor = 0,
            Flags = 0,
            Type = MessageType.Auth,
            SequenceId = 1,
            PayloadLength = 10,
            Payload = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 },
        };
        
        // Act
        await handler.HandleAsync(context, message);
        
        // Assert - with invalid JSON payload, handler should send error response
        Assert.NotEmpty(_messageSender.SentMessages);
    }
    
    [Fact]
    public async Task PingHandler_ShouldUpdateActivity()
    {
        // Arrange
        var handler = new PingHandler();
        var context = new TestConnectionContext(12345ul);
        var initialActivity = context.LastActivity;
        
        var message = new Aegis.Protocol.Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = 1,
            VersionMinor = 0,
            Flags = 0,
            Type = MessageType.Ping,
            SequenceId = 1,
            PayloadLength = 8,
            Payload = BitConverter.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
        };
        
        // Act
        await handler.HandleAsync(context, message);
        
        // Assert
        Assert.True(context.LastActivity > initialActivity,
            $"LastActivity ({context.LastActivity}) should be greater than initial ({initialActivity})");
    }
    
    [Fact]
    public async Task MessageHandler_AllowedMessage_ShouldSendAck()
    {
        // Arrange
        var handler = new MessageHandler(_antiSpam, _messageServiceMock.Object, _messageSender, _sessionManager, _logger);
        var context = new TestConnectionContext(12345ul);
        var recipientContext = new TestConnectionContext(54321ul);
        _sessionManager.CreateSession(context.ConnectionId);
        _sessionManager.EstablishHandshake(context.ConnectionId, new byte[32]);
        _sessionManager.AuthenticateSession(context.ConnectionId, 42, "tester");
        _sessionManager.CreateSession(recipientContext.ConnectionId);
        _sessionManager.EstablishHandshake(recipientContext.ConnectionId, new byte[32]);
        _sessionManager.AuthenticateSession(recipientContext.ConnectionId, 24, "recipient");

        var payload = Encoding.UTF8.GetBytes("{\"recipientId\":24,\"content\":\"Hello World!\"}");
        var message = new Aegis.Protocol.Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = 1,
            VersionMinor = 0,
            Flags = 0,
            Type = MessageType.Message,
            SequenceId = 1,
            PayloadLength = (uint)payload.Length,
            Payload = payload,
        };
        
        _antiSpam.AllowNextMessage = true;
        
        // Act
        await handler.HandleAsync(context, message);
        
        // Assert
        Assert.True(_antiSpam.CheckedConnection);
        Assert.True(handler.AckSent);
        Assert.Equal(message.SequenceId, handler.AckSequenceId);
    }
    
    [Fact]
    public async Task MessageHandler_RejectedMessage_ShouldSendError()
    {
        // Arrange
        var handler = new MessageHandler(_antiSpam, _messageServiceMock.Object, _messageSender, _sessionManager, _logger);
        var context = new TestConnectionContext(12345ul);
        _sessionManager.CreateSession(context.ConnectionId);
        _sessionManager.EstablishHandshake(context.ConnectionId, new byte[32]);
        _sessionManager.AuthenticateSession(context.ConnectionId, 42, "tester");

        var payload = Encoding.UTF8.GetBytes("{\"recipientId\":24,\"content\":\"Spam message\"}");
        var message = new Aegis.Protocol.Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = 1,
            VersionMinor = 0,
            Flags = 0,
            Type = MessageType.Message,
            SequenceId = 1,
            PayloadLength = (uint)payload.Length,
            Payload = payload,
        };
        
        _antiSpam.AllowNextMessage = false;
        
        // Act
        await handler.HandleAsync(context, message);
        
        // Assert
        Assert.True(_antiSpam.CheckedConnection);
        Assert.Contains("rejected by anti-spam", handler.ErrorMessage);
    }
    
    private class TestConnectionContext : ConnectionContext
    {
        private DateTime _lastActivity = DateTime.UtcNow;
        
        public TestConnectionContext(ulong connectionId) : base(new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp), connectionId)
        {
        }
        
        public new DateTime LastActivity 
        { 
            get => _lastActivity; 
            private set => _lastActivity = value; 
        }
        
        public override void UpdateActivity()
        {
            _lastActivity = DateTime.UtcNow;
        }
    }
    
    private class TestMessageHandler : IMessageHandler
    {
        public MessageType Type => MessageType.Message;
        
        public bool Handled { get; private set; }
        public ulong HandledConnectionId { get; private set; }
        public ulong HandledSequenceId { get; private set; }
        
        public ValueTask HandleAsync(ConnectionContext context, Aegis.Protocol.Message message)
        {
            Handled = true;
            HandledConnectionId = context.ConnectionId;
            HandledSequenceId = message.SequenceId;
            return ValueTask.CompletedTask;
        }
    }
    
    private class TestAntiSpamClient : IAntiSpamClient
    {
        public bool AllowNextMessage { get; set; } = true;
        public bool CheckedConnection { get; private set; }
        public ulong CheckedConnectionId { get; private set; }
        
        public async Task<bool> CheckMessageAsync(ulong connectionId, byte[] message)
        {
            CheckedConnection = true;
            CheckedConnectionId = connectionId;
            await Task.CompletedTask;
            return AllowNextMessage;
        }
    }
    
    private class TestLogger : Aegis.Common.Logging.ILogger
    {
        public List<string> DebugMessages { get; } = new();
        public List<string> InfoMessages { get; } = new();
        public List<string> WarningMessages { get; } = new();
        public List<string> ErrorMessages { get; } = new();
        
        public void Debug(string message) => DebugMessages.Add(message);
        public void Info(string message) => InfoMessages.Add(message);
        public void Warning(string message) => WarningMessages.Add(message);
        public void Error(string message, Exception? ex = null) 
        {
            ErrorMessages.Add(message);
            if (ex != null) ErrorMessages.Add(ex.ToString());
        }
    }

    // New tests for updated handlers
    [Fact]
    public async Task MessageRouter_ShouldRouteNewMessageTypes()
    {
        // Arrange
        var mockRegistrationService = new Mock<IUserRegistrationService>();
        var mockSearchService = new Mock<IUserSearchService>();
        var rateLimiter = new RateLimiter(new Aegis.Common.Configuration.RateLimitOptions());
        var domainRules = new DomainRulesAdapter(new Aegis.DomainRules.MessageDomainRules());

        var registrationHandler = new RegistrationHandler(mockRegistrationService.Object, _messageSender, rateLimiter, new Mock<ILogger<RegistrationHandler>>().Object);
        var searchHandler = new UserSearchHandler(mockSearchService.Object, _sessionManager, _messageSender, rateLimiter, new Mock<ILogger<UserSearchHandler>>().Object);
        var channelHandler = new ChannelMessageHandler(
            new Mock<IMessageService>().Object,
            new Mock<IChannelRepository>().Object,
            _sessionManager,
            _messageSender,
            domainRules,
            new Mock<Microsoft.Extensions.Logging.ILogger<ChannelMessageHandler>>().Object);

        var router = MessageRouter.ForHandlers(new IMessageHandler[] { registrationHandler, searchHandler, channelHandler }, _messageSender, _logger);
        var context = new TestConnectionContext(12345ul);
        _sessionManager.CreateSession(context.ConnectionId);
        _sessionManager.EstablishHandshake(context.ConnectionId, new byte[32]);
        _sessionManager.AuthenticateSession(context.ConnectionId, 1, "router-user");

        // Test Registration message routing
        var registrationRequest = new RegistrationRequest("testuser", "test@example.com", "password123", "public_key");
        var registrationPayload = JsonSerializer.SerializeToUtf8Bytes(registrationRequest);
        var registrationMessage = new Aegis.Protocol.Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = 1,
            VersionMinor = 0,
            Type = MessageType.Register,
            SequenceId = 1,
            PayloadLength = (uint)registrationPayload.Length,
            Payload = registrationPayload
        };

        // Act & Assert - Should not throw
        await router.RouteAsync(context, registrationMessage);

        // Test UserSearch message routing
        var searchRequest = new UserSearchRequest("test", 10);
        var searchPayload = JsonSerializer.SerializeToUtf8Bytes(searchRequest);
        var searchMessage = new Aegis.Protocol.Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = 1,
            VersionMinor = 0,
            Type = MessageType.UserSearch,
            SequenceId = 2,
            PayloadLength = (uint)searchPayload.Length,
            Payload = searchPayload
        };

        // Act & Assert - Should not throw
        await router.RouteAsync(context, searchMessage);

        // Test ChannelMessage message routing
        var channelRequest = new ChannelMessageRequest(1, "Hello, channel!", MessageContentType.Text);
        var channelPayload = JsonSerializer.SerializeToUtf8Bytes(channelRequest);
        var channelMessage = new Aegis.Protocol.Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = 1,
            VersionMinor = 0,
            Type = MessageType.ChannelMessage,
            SequenceId = 3,
            PayloadLength = (uint)channelPayload.Length,
            Payload = channelPayload
        };

        // Act & Assert - Should not throw
        await router.RouteAsync(context, channelMessage);
    }

    [Fact]
    public void NewHandlers_ShouldHaveCorrectMessageTypes()
    {
        // Arrange
        var mockRegistrationService = new Mock<IUserRegistrationService>();
        var mockSearchService = new Mock<IUserSearchService>();
        var rateLimiter = new RateLimiter(new Aegis.Common.Configuration.RateLimitOptions());
        var domainRules = new DomainRulesAdapter(new Aegis.DomainRules.MessageDomainRules());

        var registrationHandler = new RegistrationHandler(mockRegistrationService.Object, _messageSender, rateLimiter, new Mock<ILogger<RegistrationHandler>>().Object);
        var searchHandler = new UserSearchHandler(mockSearchService.Object, _sessionManager, _messageSender, rateLimiter, new Mock<ILogger<UserSearchHandler>>().Object);
        var channelHandler = new ChannelMessageHandler(
            new Mock<IMessageService>().Object,
            new Mock<IChannelRepository>().Object,
            _sessionManager,
            _messageSender,
            domainRules,
            new Mock<Microsoft.Extensions.Logging.ILogger<ChannelMessageHandler>>().Object);
        var channelCreateHandler = new ChannelCreateHandler(
            new Mock<IChannelService>().Object,
            _sessionManager,
            _messageSender,
            new Mock<Microsoft.Extensions.Logging.ILogger<ChannelCreateHandler>>().Object);
        var privateChatHandler = new PrivateChatMessageHandler(
            new Mock<IMessageService>().Object,
            mockSearchService.Object,
            new Mock<IBotManagementService>().Object,
            _sessionManager,
            _messageSender,
            domainRules,
            new Mock<Microsoft.Extensions.Logging.ILogger<PrivateChatMessageHandler>>().Object);

        // Act & Assert
        Assert.Equal(MessageType.Register, registrationHandler.Type);
        Assert.Equal(MessageType.UserSearch, searchHandler.Type);
        Assert.Equal(MessageType.ChannelMessage, channelHandler.Type);
        Assert.Equal(MessageType.ChannelCreate, channelCreateHandler.Type);
        Assert.Equal(MessageType.PrivateChatMessage, privateChatHandler.Type);
    }

    [Fact]
    public async Task MessageRouter_ShouldHandleUnknownMessageType()
    {
        // Arrange
        var testHandler = new TestMessageHandler();
        var router = MessageRouter.ForHandlers(new IMessageHandler[] { testHandler }, _messageSender, _logger);
        var context = new TestConnectionContext(12345ul);
        var message = new Aegis.Protocol.Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = 1,
            VersionMinor = 0,
            Type = (MessageType)999, // Unknown type
            SequenceId = 1,
            PayloadLength = 0,
            Payload = Array.Empty<byte>()
        };

        // Act & Assert - Should not throw, should handle gracefully
        await router.RouteAsync(context, message);
        
        // Should log error about unknown message type
        Assert.NotEmpty(_logger.ErrorMessages);
        Assert.Contains("Unknown message type", _logger.ErrorMessages.First());
    }
}