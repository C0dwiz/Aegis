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
    private readonly Aegis.Crypto.ICryptoProvider _cryptoProvider;
    private readonly SessionManager _sessionManager;
    
    public MessageRouter(IEnumerable<IMessageHandler> handlers, Aegis.Crypto.ICryptoProvider cryptoProvider, SessionManager sessionManager, ILogger? logger = null)
    {
        _handlers = handlers.ToDictionary(h => h.Type, h => h);
        _logger = logger ?? new Aegis.Transport.NullLogger();
        _cryptoProvider = cryptoProvider;
        _sessionManager = sessionManager;
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
        try
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
            
            // Get session for encryption
            var session = _sessionManager.GetSession(context.ConnectionId);
            if (session != null)
            {
                // Encrypt and sign the message
                var encryptedMessage = await _cryptoProvider.EncryptMessageAsync(errorMsg, session.SessionKey.ToArray());
                
                // Send the encrypted message through the transport layer
                // Note: This requires access to the TcpServer or a message sender interface
                _logger.Warning($"Error message encrypted and ready to send to connection {context.ConnectionId}: {error}");
            }
            else
            {
                _logger.Warning($"No session found for connection {context.ConnectionId}, cannot encrypt error message");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Error encrypting error message for connection {context.ConnectionId}", ex);
            throw;
        }
    }
}
