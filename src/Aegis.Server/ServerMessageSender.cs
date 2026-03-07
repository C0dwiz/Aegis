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

            if (TryDecodeProtocolMessage(encryptedMessage, out var protocolMessage))
            {
                await SendProtocolMessageAsync(
                    connectionId,
                    (ushort)protocolMessage.Type,
                    protocolMessage.SequenceId,
                    protocolMessage.Payload,
                    allowUnsigned: true);
                return;
            }

            await _server.SendToConnectionAsync(connectionId, encryptedMessage);
            _logger.Debug($"Raw message sent to connection {connectionId}, size: {encryptedMessage.Length}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Error sending message to connection {connectionId}", ex);
            throw;
        }
    }

    public async Task SendProtocolMessageAsync(ulong connectionId, ushort messageType, ulong sequenceId, byte[] payload, bool allowUnsigned = false)
    {
        if (_server == null)
        {
            _logger.Warning("TcpServer is null, cannot send protocol message");
            return;
        }

        var message = new Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = ProtocolConstants.VersionMajor,
            VersionMinor = ProtocolConstants.VersionMinor,
            Type = (MessageType)messageType,
            SequenceId = sequenceId,
            Payload = payload,
            PayloadLength = (uint)payload.Length
        };

        message.PayloadLength = (uint)message.Payload.Length;

        var session = _sessionManager.GetSession(connectionId);
        var shouldSign = session != null && session.HandshakeEstablished && !session.MacKey.IsEmpty;
        if (!shouldSign && !allowUnsigned)
        {
            _logger.Warning($"Sending unsigned message {message.Type} to connection {connectionId} because no handshake is established");
        }

        message.Mac = new byte[ProtocolConstants.MacSize];
        var buffer = new byte[Message.TotalSize(message)];
        MessageEncoder.Encode(message, buffer);

        if (shouldSign)
        {
            _cryptoProvider.ComputeMac(
                buffer.AsSpan(0, buffer.Length - ProtocolConstants.MacSize),
                session!.MacKey.Span,
                buffer.AsSpan(buffer.Length - ProtocolConstants.MacSize, ProtocolConstants.MacSize));
        }

        await _server.SendToConnectionAsync(connectionId, buffer);
        _logger.Debug($"Protocol message {message.Type} sent to connection {connectionId}, size: {buffer.Length}");
    }

    private static bool TryDecodeProtocolMessage(byte[] data, out Message message)
    {
        try
        {
            message = MessageEncoder.Decode(data);
            return Message.TotalSize(message) == data.Length;
        }
        catch
        {
            message = new Message();
            return false;
        }
    }
}
