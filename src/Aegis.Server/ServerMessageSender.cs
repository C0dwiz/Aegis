using Aegis.Transport;
using Aegis.Crypto;
using Aegis.Protocol;
using Aegis.Common.Logging;
using Aegis.Common;

namespace Aegis.Server;

public class ServerMessageSender : IMessageSender
{
    private readonly TcpServer? _server;
    private readonly ICryptoProvider _cryptoProvider;
    private readonly SessionManager _sessionManager;
    private readonly ILogger _logger;

    public ServerMessageSender(
        TcpServer? server, 
        ICryptoProvider cryptoProvider, 
        SessionManager sessionManager, 
        ILogger logger)
    {
        _server = server;
        _cryptoProvider = cryptoProvider;
        _sessionManager = sessionManager;
        _logger = logger;
    }

    public async Task SendMessageAsync(ulong connectionId, byte[] encryptedMessage)
    {
        try
        {
            // TODO: Find actual connection context by ID and send message
            // For now, just log that we would send the message
            _logger.Debug($"Message would be sent to connection {connectionId}, size: {encryptedMessage.Length}");
            
            // In a real implementation:
            // var context = _server.GetConnection(connectionId);
            // await _server.SendAsync(context, encryptedMessage);
        }
        catch (Exception ex)
        {
            _logger.Error($"Error sending message to connection {connectionId}", ex);
            throw;
        }
    }
}
