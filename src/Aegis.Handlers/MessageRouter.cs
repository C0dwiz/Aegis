using Aegis.Protocol;
using Aegis.Transport;
using Aegis.Common.Logging;

namespace Aegis.Handlers;

public class MessageRouter
{
    private readonly Dictionary<MessageType, IMessageHandler> _handlers;
    private readonly ILogger _logger;
    
    public MessageRouter(IEnumerable<IMessageHandler> handlers, ILogger? logger = null)
    {
        _handlers = handlers.ToDictionary(h => h.Type, h => h);
        _logger = logger ?? new Aegis.Transport.NullLogger();
    }
    
    public async ValueTask RouteAsync(ConnectionContext context, Message message)
    {
        if (_handlers.TryGetValue(message.Type, out var handler))
        {
            await handler.HandleAsync(context, message);
        }
        else
        {
            _logger.Error($"Unknown message type: {message.Type}");
            // отправка ошибки
            await SendErrorAsync(context, message.SequenceId, $"Unknown message type: {message.Type}");
        }
    }
    
    private static async Task SendErrorAsync(ConnectionContext context, ulong sequenceId, string error)
    {
        var errorMsg = new Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = ProtocolConstants.VersionMajor,
            VersionMinor = ProtocolConstants.VersionMinor,
            Type = MessageType.Error,
            SequenceId = sequenceId,
            Payload = System.Text.Encoding.UTF8.GetBytes(error)
        };
        
        // TODO: Encrypt and sign the message
        await Task.CompletedTask;
    }
}
