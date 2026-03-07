using Aegis.Protocol;
using Aegis.Transport;
using Aegis.Common.Logging;
using Aegis.Common;
using Aegis.Crypto;

namespace Aegis.Handlers;

public class MessageRouter
{
    private readonly Dictionary<Aegis.Protocol.MessageType, IMessageHandler> _handlers;
    private readonly ILogger _logger;
    private readonly IMessageSender _messageSender;
    
    public MessageRouter(IEnumerable<IMessageHandler> handlers, IMessageSender messageSender, ILogger? logger = null)
    {
        _handlers = handlers.ToDictionary(h => h.Type, h => h);
        _logger = logger ?? new Aegis.Transport.NullLogger();
        _messageSender = messageSender;
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
    
    private async Task SendErrorAsync(ConnectionContext context, ulong sequenceId, string error)
    {
        var errorMsg = new Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = ProtocolConstants.VersionMajor,
            VersionMinor = ProtocolConstants.VersionMinor,
            Type = Aegis.Protocol.MessageType.Error,
            SequenceId = sequenceId,
            PayloadLength = (uint)System.Text.Encoding.UTF8.GetByteCount(error),
            Payload = System.Text.Encoding.UTF8.GetBytes(error)
        };

        await _messageSender.SendProtocolMessageAsync(
            context.ConnectionId,
            (ushort)Aegis.Protocol.MessageType.Error,
            sequenceId,
            System.Text.Encoding.UTF8.GetBytes(error),
            allowUnsigned: true);
    }
}
