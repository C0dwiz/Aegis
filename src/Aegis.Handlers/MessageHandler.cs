using Aegis.Protocol;
using Aegis.Transport;

namespace Aegis.Handlers;

public class MessageHandler : IMessageHandler
{
    private readonly IAntiSpamClient _antiSpam;
    private bool _ackSent = false;
    private ulong _ackSequenceId = 0;
    private string _errorMessage = string.Empty;
    
    public MessageType Type => MessageType.Message;
    
    public MessageHandler(IAntiSpamClient antiSpam)
    {
        _antiSpam = antiSpam;
    }
    
    public async ValueTask HandleAsync(ConnectionContext context, Message message)
    {
        var allowed = await _antiSpam.CheckMessageAsync(context.ConnectionId, message.Payload);
        
        if (!allowed)
        {
            // отклонение сообщения
            await SendErrorAsync(context, message.SequenceId, "Message rejected by anti-spam");
            return;
        }
        
        // TODO: Обрабатывать и маршрутизировать сообщение получателю
        await SendAckAsync(context, message.SequenceId);
    }
    
    public bool AckSent => _ackSent;
    public ulong AckSequenceId => _ackSequenceId;
    public string ErrorMessage => _errorMessage;
    
    private async Task SendAckAsync(ConnectionContext context, ulong sequenceId)
    {
        // потверждение получения сообщения
        _ackSent = true;
        _ackSequenceId = sequenceId;
        await Task.CompletedTask;
    }
    
    private async Task SendErrorAsync(ConnectionContext context, ulong sequenceId, string error)
    {
        // TODO: отправка ошибки клиенту
        _errorMessage = error;
        await Task.CompletedTask;
    }
}
