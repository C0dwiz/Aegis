using Aegis.Protocol;
using Aegis.Transport;

namespace Aegis.Handlers;

public class PingHandler : IMessageHandler
{
    public MessageType Type => MessageType.Ping;

    public ValueTask HandleAsync(ConnectionContext context, Message message)
    {
        // Ping просто обновляет активность, ответ не нужен
        context.UpdateActivity();
        return ValueTask.CompletedTask;
    }
}
