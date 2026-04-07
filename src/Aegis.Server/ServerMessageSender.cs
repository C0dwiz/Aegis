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

        var session = _sessionManager.GetSession(connectionId);
        var shouldEncrypt =
            _protocolSecurityOptions.EncryptServerPayloadsAfterHandshake &&
            session != null &&
            session.HandshakeEstablished &&
            !session.SessionKey.IsEmpty &&
            (MessageType)messageType != MessageType.Handshake;

        // Compress raw payload before encryption if it exceeds the threshold.
        // Track the compressed buffer separately so we can zero it after encryption.
        byte flags = (byte)MessageFlags.None;
        byte[]? compressedBuffer = null;
        if (payload.Length > ProtocolConstants.CompressionThreshold)
        {
            var compressed = CompressBrotli(payload);
            if (compressed.Length < payload.Length)
            {
                compressedBuffer = compressed;
                payload = compressed;
                flags = (byte)(flags | (byte)MessageFlags.Compressed);
            }
        }

        if (shouldEncrypt)
        {
            var nonce = new byte[12];
            RandomNumberGenerator.Fill(nonce);

            var ciphertextWithTag = new byte[payload.Length + 16];
            var encryptedPayload = new byte[nonce.Length + ciphertextWithTag.Length];
            var aadMessage = new Message
            {
                Magic = ProtocolConstants.Magic,
                VersionMajor = ProtocolConstants.VersionMajor,
                VersionMinor = ProtocolConstants.VersionMinor,
                Flags = (byte)(flags | (byte)MessageFlags.Encrypted),
                Type = (MessageType)messageType,
                SequenceId = sequenceId,
                Payload = Array.Empty<byte>(),
                PayloadLength = (uint)encryptedPayload.Length
            };
            var aadHeader = new byte[ProtocolConstants.HeaderSize];
            MessageEncoder.EncodeHeader(aadMessage, aadHeader);

            _cryptoProvider.Encrypt(payload, session!.SessionKey.Span, nonce, ciphertextWithTag,
                aadHeader.AsSpan(0, ProtocolConstants.HeaderSize));

            Buffer.BlockCopy(nonce, 0, encryptedPayload, 0, nonce.Length);
            Buffer.BlockCopy(ciphertextWithTag, 0, encryptedPayload, nonce.Length, ciphertextWithTag.Length);

            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(ciphertextWithTag);
            CryptographicOperations.ZeroMemory(aadHeader);
            // Zero the compressed plaintext copy we allocated; cannot zero the caller's original buffer.
            if (compressedBuffer != null)
                CryptographicOperations.ZeroMemory(compressedBuffer);

            payload = encryptedPayload;
            flags = (byte)(flags | (byte)MessageFlags.Encrypted);
        }

        var message = new Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = ProtocolConstants.VersionMajor,
            VersionMinor = ProtocolConstants.VersionMinor,
            Flags = flags,
            Type = (MessageType)messageType,
            SequenceId = sequenceId,
            Payload = payload,
            PayloadLength = (uint)payload.Length
        };

        var totalSize = Message.TotalSize(message);
        var rented = ArrayPool<byte>.Shared.Rent(totalSize);

        try
        {
            var output = rented.AsMemory(0, totalSize);
            MessageEncoder.Encode(message, output.Span);
            await _server.SendToConnectionAsync(connectionId, output);
            _logger.Debug($"Protocol message {message.Type} sent to connection {connectionId}, size: {totalSize}");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static byte[] CompressBrotli(byte[] data)
    {
        using var output = new System.IO.MemoryStream();
        using (var brotli = new System.IO.Compression.BrotliStream(output, System.IO.Compression.CompressionLevel.Fastest))
        {
            brotli.Write(data, 0, data.Length);
        }
        return output.ToArray();
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
