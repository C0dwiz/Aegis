using Aegis.Transport;
using Aegis.Crypto;
using Aegis.Protocol;
using Aegis.Common.Logging;
using Aegis.Common;

namespace Aegis.Server;

public class ServerMessageSender : IMessageSender
{
    private readonly TcpServer? _server;
    private readonly Aegis.Crypto.ICryptoProvider _cryptoProvider;
    private readonly SessionManager _sessionManager;
    private readonly ILogger _logger;

    public ServerMessageSender(
        TcpServer? server, 
        Aegis.Crypto.ICryptoProvider cryptoProvider, 
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
            if (_server == null)
            {
                _logger.Warning("TcpServer is null, cannot send message");
                return;
            }

            await _server.SendToConnectionAsync(connectionId, encryptedMessage);
            _logger.Debug($"Message sent to connection {connectionId}, size: {encryptedMessage.Length}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Error sending message to connection {connectionId}", ex);
            throw;
        }
    }
}
