using System.Text;
using Aegis.Common;
using Aegis.Crypto;
using Aegis.Protocol;
using Aegis.Transport;
using Microsoft.Extensions.Logging;

namespace Aegis.Handlers;

public record HandshakeRequestPayload(string PublicKey, int? ClientVersion = null);

public record HandshakeResponsePayload(bool Success, string? ServerPublicKey = null, string? Message = null);

public class HandshakeHandler : IMessageHandler
{
    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;
    private readonly Aegis.Crypto.ICryptoProvider _cryptoProvider;
    private readonly ILogger<HandshakeHandler> _logger;

    public MessageType Type => MessageType.Handshake;

    public HandshakeHandler(
        SessionManager sessionManager,
        IMessageSender messageSender,
        Aegis.Crypto.ICryptoProvider cryptoProvider,
        ILogger<HandshakeHandler> logger)
    {
        _sessionManager = sessionManager;
        _messageSender = messageSender;
        _cryptoProvider = cryptoProvider;
        _logger = logger;
    }

    public async ValueTask HandleAsync(ConnectionContext context, Message message)
    {
        try
        {
            var clientPublicKey = ExtractClientPublicKey(message.Payload);
            if (clientPublicKey.Length == 0)
            {
                await SendHandshakeResponseAsync(context, message.SequenceId, false, null, "Invalid handshake payload", allowUnsigned: true);
                return;
            }

            using var keyExchange = EcdhKeyExchange.GenerateKeyPair();
            var sharedSecret = keyExchange.ComputeSharedSecret(clientPublicKey);
            Span<byte> sessionKey = stackalloc byte[32];
            _cryptoProvider.DeriveKeys(sharedSecret, sessionKey);

            if (!_sessionManager.EstablishHandshake(context.ConnectionId, sessionKey))
            {
                await SendHandshakeResponseAsync(context, message.SequenceId, false, null, "Unable to establish handshake", allowUnsigned: true);
                return;
            }

            await SendHandshakeResponseAsync(
                context,
                message.SequenceId,
                true,
                Convert.ToBase64String(keyExchange.PublicKey),
                "Handshake established",
                allowUnsigned: false);

            _logger.LogInformation("Handshake completed for connection {ConnectionId}", context.ConnectionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Handshake failed for connection {ConnectionId}", context.ConnectionId);
            await SendHandshakeResponseAsync(context, message.SequenceId, false, null, "Handshake failed", allowUnsigned: true);
        }
    }

    private static byte[] ExtractClientPublicKey(byte[] payload)
    {
        if (payload.Length == 0)
        {
            return Array.Empty<byte>();
        }

        try
        {
            var request = PayloadSerializer.Deserialize<HandshakeRequestPayload>(payload);
            if (request != null && !string.IsNullOrWhiteSpace(request.PublicKey))
            {
                return Convert.FromBase64String(request.PublicKey);
            }
        }
        catch
        {
        }

        if (payload.Length > 16)
        {
            var legacyKey = payload.AsSpan(16).ToArray();
            if (TryDecodeBase64(Encoding.UTF8.GetString(legacyKey), out var decoded))
            {
                return decoded;
            }

            return legacyKey;
        }

        return Array.Empty<byte>();
    }

    private async Task SendHandshakeResponseAsync(
        ConnectionContext context,
        ulong sequenceId,
        bool success,
        string? serverPublicKey,
        string message,
        bool allowUnsigned)
    {
        var payload = PayloadSerializer.Serialize(new HandshakeResponsePayload(success, serverPublicKey, message));
        await _messageSender.SendProtocolMessageAsync(
            context.ConnectionId,
            (ushort)MessageType.Handshake,
            sequenceId,
            payload,
            allowUnsigned);
    }

    private static bool TryDecodeBase64(string value, out byte[] decoded)
    {
        try
        {
            decoded = Convert.FromBase64String(value);
            return true;
        }
        catch
        {
            decoded = Array.Empty<byte>();
            return false;
        }
    }
}
