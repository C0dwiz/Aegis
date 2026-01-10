using Aegis.Protocol;
using Aegis.Transport;

namespace Aegis.Handlers;

public class AuthHandler : IMessageHandler
{
    private readonly IAntiSpamClient _antiSpam;
    
    public MessageType Type => MessageType.Auth;
    
    public AuthHandler(IAntiSpamClient antiSpam)
    {
        _antiSpam = antiSpam;
    }
    
    public async ValueTask HandleAsync(ConnectionContext context, Message message)
    {
        await _antiSpam.CheckMessageAsync(context.ConnectionId, message.Payload);
        
        // TODO: Implement authentication logic
        // For now, just acknowledge
        await SendAckAsync(context, message.SequenceId);
    }
    
    private static async Task SendAckAsync(ConnectionContext context, ulong sequenceId)
    {
        var ack = new Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = ProtocolConstants.VersionMajor,
            VersionMinor = ProtocolConstants.VersionMinor,
            Type = MessageType.Ack,
            SequenceId = sequenceId
        };
        
        // TODO: Send acknowledgment
        await Task.CompletedTask;
    }
}
