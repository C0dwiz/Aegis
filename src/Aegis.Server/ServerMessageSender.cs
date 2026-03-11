using Aegis.Transport;
using Aegis.Crypto;
using Aegis.Protocol;
using Aegis.Common.Logging;
using Aegis.Common;
using Aegis.Common.Configuration;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Buffers;

namespace Aegis.Server;

public class ServerMessageSender : IMessageSender
{
    private readonly TcpServer? _server;
    private readonly Aegis.Crypto.ICryptoProvider _cryptoProvider;
    private readonly SessionManager _sessionManager;
    private readonly ProtocolSecurityOptions _protocolSecurityOptions;
    private readonly ILogger _logger;

    public ServerMessageSender(
        TcpServer? server, 
        Aegis.Crypto.ICryptoProvider cryptoProvider, 
        SessionManager sessionManager, 
        IOptions<ProtocolSecurityOptions> protocolSecurityOptions,
        ILogger logger)
    {
        _server = server;
        _cryptoProvider = cryptoProvider;
        _sessionManager = sessionManager;
        _protocolSecurityOptions = protocolSecurityOptions.Value;
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
            Flags = (byte)MessageFlags.None,
            Type = (MessageType)messageType,
            SequenceId = sequenceId,
            Payload = payload,
            PayloadLength = (uint)payload.Length
        };

        message.PayloadLength = (uint)message.Payload.Length;

        var session = _sessionManager.GetSession(connectionId);
        var shouldSign = session != null && session.HandshakeEstablished && !session.MacKey.IsEmpty;
        var shouldEncrypt =
            _protocolSecurityOptions.EncryptServerPayloadsAfterHandshake &&
            session != null &&
            session.HandshakeEstablished &&
            !session.SessionKey.IsEmpty &&
            message.Type != MessageType.Handshake;

        if (shouldEncrypt)
        {
            var nonce = new byte[12];
            RandomNumberGenerator.Fill(nonce);

            var ciphertextWithTag = new byte[message.Payload.Length + 16];
            _cryptoProvider.Encrypt(message.Payload, session!.SessionKey.Span, nonce, ciphertextWithTag);

            var encryptedPayload = new byte[nonce.Length + ciphertextWithTag.Length];
            Buffer.BlockCopy(nonce, 0, encryptedPayload, 0, nonce.Length);
            Buffer.BlockCopy(ciphertextWithTag, 0, encryptedPayload, nonce.Length, ciphertextWithTag.Length);

            message.Payload = encryptedPayload;
            message.PayloadLength = (uint)encryptedPayload.Length;
            message.Flags = (byte)(message.Flags | (byte)MessageFlags.Encrypted);

            CryptographicOperations.ZeroMemory(ciphertextWithTag);
        }

        if (!shouldSign && !allowUnsigned)
        {
            _logger.Warning($"Sending unsigned message {message.Type} to connection {connectionId} because no handshake is established");
        }

        message.Mac = new byte[ProtocolConstants.MacSize];
        var totalSize = Message.TotalSize(message);
        var rented = ArrayPool<byte>.Shared.Rent(totalSize);

        try
        {
            var output = rented.AsMemory(0, totalSize);
            MessageEncoder.Encode(message, output.Span);

            if (shouldSign)
            {
                _cryptoProvider.ComputeMac(
                    output.Span.Slice(0, totalSize - ProtocolConstants.MacSize),
                    session!.MacKey.Span,
                    output.Span.Slice(totalSize - ProtocolConstants.MacSize, ProtocolConstants.MacSize));
            }

            await _server.SendToConnectionAsync(connectionId, output);
            _logger.Debug($"Protocol message {message.Type} sent to connection {connectionId}, size: {totalSize}");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
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
