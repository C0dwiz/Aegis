using Xunit;
using System.Threading.Tasks;
using Aegis.Handlers;
using Aegis.Protocol;
using Aegis.Transport;
using Aegis.Common.Logging;

namespace Aegis.Tests;

public class HandlerTests
{
    private readonly TestLogger _logger = new TestLogger();
    private readonly TestAntiSpamClient _antiSpam = new TestAntiSpamClient();
    
    [Fact]
    public async Task MessageRouter_RegisterHandler_ShouldRouteCorrectly()
    {
        // Arrange
        var testHandler = new TestMessageHandler();
        var router = new MessageRouter(new IMessageHandler[] { testHandler });
        var context = new TestConnectionContext(12345ul);
        var message = new Message
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
        
        // Act - handler already registered via constructor
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
        var router = new MessageRouter(Array.Empty<IMessageHandler>(), _logger);
        var context = new TestConnectionContext(12345ul);
        var message = new Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = 1,
            VersionMinor = 0,
            Flags = 0,
            Type = (MessageType)999, // Unknown type
            SequenceId = 1,
            PayloadLength = 0,
            Payload = Array.Empty<byte>(),
            Mac = new byte[ProtocolConstants.MacSize]
        };
        
        // Act
        await router.RouteAsync(context, message);
        
        // Assert
        Assert.Single(_logger.ErrorMessages);
        Assert.Contains("Unknown message type", _logger.ErrorMessages[0]);
    }
    
    [Fact]
    public async Task AuthHandler_ShouldProcessAuthMessages()
    {
        // Arrange
        var handler = new AuthHandler(_antiSpam);
        var context = new TestConnectionContext(12345ul);
        var message = new Message
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
        await Task.Delay(1); // Небольшая задержка
        var initialActivity = context.LastActivity;
        
        var message = new Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = 1,
            VersionMinor = 0,
            Flags = 0,
            Type = MessageType.Ping,
            SequenceId = 1,
            PayloadLength = 8,
            Payload = System.BitConverter.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
            Mac = new byte[ProtocolConstants.MacSize]
        };
        
        // Act
        await handler.HandleAsync(context, message);
        
        // Debug output
        Console.WriteLine($"Initial: {initialActivity}, Final: {context.LastActivity}, Greater: {context.LastActivity > initialActivity}");
        
        // Assert
        Assert.True(context.LastActivity > initialActivity);
    }
    
    [Fact]
    public async Task MessageHandler_AllowedMessage_ShouldSendAck()
    {
        // Arrange
        var handler = new MessageHandler(_antiSpam);
        var context = new TestConnectionContext(12345ul);
        var message = new Message
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
        var handler = new MessageHandler(_antiSpam);
        var context = new TestConnectionContext(12345ul);
        var message = new Message
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
        
        public TestConnectionContext(ulong connectionId) : base(new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp), connectionId)
        {
        }
        
        public new DateTime LastActivity 
        { 
            get => _lastActivity; 
            private set => _lastActivity = value; 
        }
        
        public override void UpdateActivity()
        {
            Console.WriteLine("TestConnectionContext.UpdateActivity() called");
            _lastActivity = DateTime.UtcNow;
            base.UpdateActivity(); // Also update base for consistency
        }
    }
    
    private class TestMessageHandler : IMessageHandler
    {
        public MessageType Type => MessageType.Message;
        
        public bool Handled { get; private set; }
        public ulong HandledConnectionId { get; private set; }
        public ulong HandledSequenceId { get; private set; }
        
        public ValueTask HandleAsync(ConnectionContext context, Message message)
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
            await Task.Delay(1); // Simulate network delay
            return AllowNextMessage;
        }
    }
    
    private class TestLogger : ILogger
    {
        public List<string> DebugMessages { get; } = new();
        public List<string> InfoMessages { get; } = new();
        public List<string> WarningMessages { get; } = new();
        public List<string> ErrorMessages { get; } = new();
        
        public void Debug(string message) => DebugMessages.Add(message);
        public void Info(string message) => InfoMessages.Add(message);
        public void Warning(string message) => WarningMessages.Add(message);
        public void Error(string message, System.Exception? ex = null) 
        {
            ErrorMessages.Add(message);
            if (ex != null) ErrorMessages.Add(ex.ToString());
        }
    }
}
