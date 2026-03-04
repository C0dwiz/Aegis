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
}

public class HandlerTests
{
    private readonly TestLogger _logger = new TestLogger();
    private readonly TestAntiSpamClient _antiSpam = new TestAntiSpamClient();
    private readonly TestMessageSender _messageSender = new TestMessageSender();
    private readonly AegisCryptoProvider _cryptoProvider = new AegisCryptoProvider();
    private readonly SessionManager _sessionManager;
    
    public HandlerTests()
    {
        _sessionManager = new SessionManager(_cryptoProvider, _logger);
    }
    
    [Fact]
    public async Task MessageRouter_RegisterHandler_ShouldRouteCorrectly()
    {
        // Arrange
        var testHandler = new TestMessageHandler();
        var router = new MessageRouter(new IMessageHandler[] { testHandler }, _cryptoProvider, _sessionManager, _logger);
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
            Mac = new byte[ProtocolConstants.MacSize]
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
        var router = new MessageRouter(Array.Empty<IMessageHandler>(), _cryptoProvider, _sessionManager, _logger);
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
            Mac = new byte[ProtocolConstants.MacSize]
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
        var handler = new AuthHandler(_antiSpam, _messageSender, _cryptoProvider, _logger);
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
            Mac = new byte[ProtocolConstants.MacSize]
        };
        
        // Act
        await handler.HandleAsync(context, message);
        
        // Assert
        Assert.True(_antiSpam.CheckedConnection);
        Assert.Equal(12345ul, _antiSpam.CheckedConnectionId);
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
            Mac = new byte[ProtocolConstants.MacSize]
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
        var handler = new MessageHandler(_antiSpam, _messageSender, _cryptoProvider, _logger);
        var context = new TestConnectionContext(12345ul);
        var message = new Aegis.Protocol.Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = 1,
            VersionMinor = 0,
            Flags = 0,
            Type = MessageType.Message,
            SequenceId = 1,
            PayloadLength = 12,
            Payload = "Hello World!"u8.ToArray(),
            Mac = new byte[ProtocolConstants.MacSize]
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
        var handler = new MessageHandler(_antiSpam, _messageSender, _cryptoProvider, _logger);
        var context = new TestConnectionContext(12345ul);
        var message = new Aegis.Protocol.Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = 1,
            VersionMinor = 0,
            Flags = 0,
            Type = MessageType.Message,
            SequenceId = 1,
            PayloadLength = 12,
            Payload = "Spam message"u8.ToArray(),
            Mac = new byte[ProtocolConstants.MacSize]
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
        var mockChannelRepository = new Mock<IChannelRepository>();
        var mockPrivateChatRepository = new Mock<IPrivateChatRepository>();

        var registrationHandler = new RegistrationHandler(mockRegistrationService.Object, new Mock<ILogger<RegistrationHandler>>().Object);
        var searchHandler = new UserSearchHandler(mockSearchService.Object, new Mock<ILogger<UserSearchHandler>>().Object);
        var channelHandler = new ChannelMessageHandler(mockChannelRepository.Object, mockSearchService.Object, new Mock<ILogger<ChannelMessageHandler>>().Object);

        var router = new MessageRouter(new IMessageHandler[] { registrationHandler, searchHandler, channelHandler }, _cryptoProvider, _sessionManager, _logger);
        var context = new TestConnectionContext(12345ul);

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
        var mockChannelRepository = new Mock<IChannelRepository>();
        var mockPrivateChatRepository = new Mock<IPrivateChatRepository>();

        var registrationHandler = new RegistrationHandler(mockRegistrationService.Object, new Mock<ILogger<RegistrationHandler>>().Object);
        var searchHandler = new UserSearchHandler(mockSearchService.Object, new Mock<ILogger<UserSearchHandler>>().Object);
        var channelHandler = new ChannelMessageHandler(mockChannelRepository.Object, mockSearchService.Object, new Mock<ILogger<ChannelMessageHandler>>().Object);
        var channelCreateHandler = new ChannelCreateHandler(mockChannelRepository.Object, mockSearchService.Object, new Mock<ILogger<ChannelCreateHandler>>().Object);
        var privateChatHandler = new PrivateChatMessageHandler(mockPrivateChatRepository.Object, mockSearchService.Object, new Mock<ILogger<PrivateChatMessageHandler>>().Object);

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
        var router = new MessageRouter(new IMessageHandler[] { testHandler }, _cryptoProvider, _sessionManager, _logger);
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