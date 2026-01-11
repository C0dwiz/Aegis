using Aegis.Protocol;
using Aegis.Transport;
using Aegis.Common.Logging;
using Aegis.Common;

namespace Aegis.Handlers;

public class MessageHandler : IMessageHandler
{
    private readonly IAntiSpamClient _antiSpam;
    private readonly IMessageSender _messageSender;
    private readonly Aegis.Crypto.ICryptoProvider _cryptoProvider;
    private readonly ILogger _logger;
    private bool _ackSent = false;
    private ulong _ackSequenceId = 0;
    private string _errorMessage = string.Empty;
    
    public Aegis.Protocol.MessageType Type => Aegis.Protocol.MessageType.Message;
    
    public MessageHandler(IAntiSpamClient antiSpam, IMessageSender messageSender, Aegis.Crypto.ICryptoProvider cryptoProvider, ILogger? logger = null)
    {
        _antiSpam = antiSpam;
        _messageSender = messageSender;
        _cryptoProvider = cryptoProvider;
        _logger = logger ?? new Aegis.Transport.NullLogger();
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
        
        // Обрабатывать и маршрутизировать сообщение получателю
        await RouteMessageToRecipient(context, message);
        await SendAckAsync(context, message.SequenceId);
    }
    
    private async Task RouteMessageToRecipient(ConnectionContext context, Message message)
    {
        try
        {
            // Десериализуем payload для определения получателя
            var messageContent = System.Text.Encoding.UTF8.GetString(message.Payload.ToArray());
            
            // TODO: Здесь должна быть логика определения получателя из сообщения
            // Для примера, предполагаем что сообщение содержит JSON с полем "recipientId"
            // и само сообщение в поле "content"
            
            _logger.Info($"Processing message from connection {context.ConnectionId}: {messageContent}");
            
            // В реальной реализации здесь была бы логика:
            // 1. Определить получателя из сообщения
            // 2. Найти активное соединение получателя
            // 3. Отправить сообщение получателю
            // 4. Сохранить сообщение в базе данных для офлайн пользователей
            
            // Сейчас просто логируем сообщение
            _logger.Info($"Message routed successfully from {context.ConnectionId}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Error routing message from connection {context.ConnectionId}", ex);
            throw;
        }
    }
    
    public bool AckSent => _ackSent;
    public ulong AckSequenceId => _ackSequenceId;
    public string ErrorMessage => _errorMessage;
    
    private async Task SendAckAsync(ConnectionContext context, ulong sequenceId)
    {
        try
        {
            var ackMessage = new Message
            {
                Magic = ProtocolConstants.Magic,
                VersionMajor = ProtocolConstants.VersionMajor,
                VersionMinor = ProtocolConstants.VersionMinor,
                Type = Aegis.Protocol.MessageType.Ack,
                SequenceId = sequenceId,
                PayloadLength = 0,
                Payload = Array.Empty<byte>(),
                Mac = new byte[ProtocolConstants.MacSize]
            };
            
            // Encrypt and send through message sender
            var sessionKey = new byte[32]; // TODO: Get from session manager
            var encryptedMessage = await _cryptoProvider.EncryptMessageAsync(ackMessage, sessionKey);
            await _messageSender.SendMessageAsync(context.ConnectionId, encryptedMessage);
            
            _ackSent = true;
            _ackSequenceId = sequenceId;
            
            _logger.Debug($"ACK sent for sequence {sequenceId} to connection {context.ConnectionId}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Error sending ACK to connection {context.ConnectionId}", ex);
            throw;
        }
    }
    
    private async Task SendErrorAsync(ConnectionContext context, ulong sequenceId, string error)
    {
        try
        {
            var errorMessage = new Message
            {
                Magic = ProtocolConstants.Magic,
                VersionMajor = ProtocolConstants.VersionMajor,
                VersionMinor = ProtocolConstants.VersionMinor,
                Type = Aegis.Protocol.MessageType.Error,
                SequenceId = sequenceId,
                PayloadLength = (uint)System.Text.Encoding.UTF8.GetByteCount(error),
                Payload = System.Text.Encoding.UTF8.GetBytes(error),
                Mac = new byte[ProtocolConstants.MacSize]
            };
            
            // Encrypt and send through message sender
            var sessionKey = new byte[32]; // TODO: Get from session manager
            var encryptedMessage = await _cryptoProvider.EncryptMessageAsync(errorMessage, sessionKey);
            await _messageSender.SendMessageAsync(context.ConnectionId, encryptedMessage);
            
            _errorMessage = error;
            
            _logger.Warning($"Error sent to connection {context.ConnectionId}: {error}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Error sending error to connection {context.ConnectionId}", ex);
            throw;
        }
    }
}
