using Aegis.Protocol;
using Aegis.Transport;

namespace Aegis.Handlers;

public interface IMessageHandler
{
    MessageType Type { get; }
    ValueTask HandleAsync(ConnectionContext context, Message message);
}
