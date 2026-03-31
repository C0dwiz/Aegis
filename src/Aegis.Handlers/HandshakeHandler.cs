using System.Text;
using Aegis.Common;
using Aegis.Crypto;
using Aegis.Protocol;
using Aegis.Transport;
using Aegis.Common.Configuration;
using Aegis.Data.Services;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Buffers.Binary;

namespace Aegis.Handlers;

/// <summary>
/// Handshake request sent by the client.
/// AppId and AppHash are optional unless ProtocolSecurity:RequireAppCredentials = true.
/// </summary>
public record HandshakeRequestPayload(
    string PublicKey,
    int? ClientVersion = null,
    int? AppId = null,
    string? AppHash = null);

public record HandshakeResponsePayload(
    bool Success,
    string? ServerPublicKey = null,
    string? Message = null,
    string? Signature = null,
    string? SignatureAlgorithm = null);

public class HandshakeHandler : IMessageHandler
{
    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;
    private readonly Aegis.Crypto.ICryptoProvider _cryptoProvider;
    private readonly ProtocolSecurityOptions _protocolSecurityOptions;
    private readonly IAppCredentialService? _appCredentialService;
    private readonly ILogger<HandshakeHandler> _logger;

    public MessageType Type => MessageType.Handshake;

    public HandshakeHandler(
        SessionManager sessionManager,
        IMessageSender messageSender,
        Aegis.Crypto.ICryptoProvider cryptoProvider,
        IOptions<ProtocolSecurityOptions> protocolSecurityOptions,
        ILogger<HandshakeHandler> logger,
        IAppCredentialService? appCredentialService = null)
    {
        _sessionManager = sessionManager;
        _messageSender = messageSender;
        _cryptoProvider = cryptoProvider;
        _protocolSecurityOptions = protocolSecurityOptions.Value;
        _appCredentialService = appCredentialService;
        _logger = logger;
    }

    public async ValueTask HandleAsync(ConnectionContext context, Message message)
    {
        try
        {
            var clientPublicKey = ExtractClientPublicKey(message.Payload, out var parsedRequest);
            if (clientPublicKey.Length == 0)
            {
                await SendHandshakeResponseAsync(context, message.SequenceId, false, null, "Invalid handshake payload", allowUnsigned: true);
                return;
            }

            // Validate app credentials when enforcement is enabled
            if (_protocolSecurityOptions.RequireAppCredentials)
            {
                if (_appCredentialService == null)
                {
                    _logger.LogError("RequireAppCredentials is enabled but IAppCredentialService is not registered");
                    await SendHandshakeResponseAsync(context, message.SequenceId, false, null, "Server configuration error", allowUnsigned: true);
                    return;
                }

                if (parsedRequest?.AppId == null || string.IsNullOrWhiteSpace(parsedRequest.AppHash))
                {
                    _logger.LogWarning("Handshake rejected: missing AppId/AppHash from {ConnectionId}", context.ConnectionId);
                    await SendHandshakeResponseAsync(context, message.SequenceId, false, null, "App credentials required", allowUnsigned: true);
                    return;
                }

                var credential = await _appCredentialService.ValidateCredentialsAsync(
                    parsedRequest.AppId.Value, parsedRequest.AppHash);

                if (credential == null)
                {
                    _logger.LogWarning(
                        "Handshake rejected: invalid AppId={AppId} from {ConnectionId}",
                        parsedRequest.AppId, context.ConnectionId);
                    await SendHandshakeResponseAsync(context, message.SequenceId, false, null, "Invalid app credentials", allowUnsigned: true);
                    return;
                }

                _logger.LogDebug(
                    "App credential validated: AppId={AppId} ({ShortName}) on {ConnectionId}",
                    credential.AppId, credential.ShortName, context.ConnectionId);
            }
            else if (parsedRequest?.AppId != null && !string.IsNullOrWhiteSpace(parsedRequest.AppHash)
                     && _appCredentialService != null)
            {
                // Credentials are optional but were provided — validate and log, don't reject
                var credential = await _appCredentialService.ValidateCredentialsAsync(
                    parsedRequest.AppId.Value, parsedRequest.AppHash);
                if (credential == null)
                {
                    _logger.LogWarning(
                        "Handshake: unrecognised AppId={AppId} from {ConnectionId} (non-enforced mode, continuing)",
                        parsedRequest.AppId, context.ConnectionId);
                }
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

            var serverPublicKey = keyExchange.PublicKeyRaw;
            var signature = SignHandshake(serverPublicKey, clientPublicKey);
            if (_protocolSecurityOptions.RequireSignedHandshakeResponses && signature == null)
            {
                await SendHandshakeResponseAsync(context, message.SequenceId, false, null, "Handshake signature unavailable", allowUnsigned: true);
                return;
            }

            await SendHandshakeResponseAsync(
                context,
                message.SequenceId,
                true,
                Convert.ToBase64String(serverPublicKey),
                "Handshake established",
                allowUnsigned: false,
                signature: signature);

            _logger.LogInformation("Handshake completed for connection {ConnectionId}", context.ConnectionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Handshake failed for connection {ConnectionId}", context.ConnectionId);
            await SendHandshakeResponseAsync(context, message.SequenceId, false, null, "Handshake failed", allowUnsigned: true);
        }
    }

    private static byte[] ExtractClientPublicKey(byte[] payload, out HandshakeRequestPayload? parsed)
    {
        parsed = null;
        if (payload.Length == 0)
        {
            return Array.Empty<byte>();
        }

        try
        {
            var request = PayloadSerializer.Deserialize<HandshakeRequestPayload>(payload);
            if (request != null && !string.IsNullOrWhiteSpace(request.PublicKey))
            {
                parsed = request;
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
        bool allowUnsigned,
        string? signature = null)
    {
        var payload = PayloadSerializer.Serialize(new HandshakeResponsePayload(
            success,
            serverPublicKey,
            message,
            signature,
            signature == null ? null : "ECDSA_P256_SHA256"));
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

    private string? SignHandshake(ReadOnlySpan<byte> serverPublicKey, ReadOnlySpan<byte> clientPublicKey)
    {
        var base64PrivateKey = _protocolSecurityOptions.HandshakeSigningPrivateKeyBase64;
        if (string.IsNullOrWhiteSpace(base64PrivateKey))
        {
            return null;
        }

        try
        {
            var privateKey = Convert.FromBase64String(base64PrivateKey);
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportPkcs8PrivateKey(privateKey, out _);
            var transcript = BuildSignatureTranscript(serverPublicKey, clientPublicKey);
            var signature = ecdsa.SignData(transcript, HashAlgorithmName.SHA256);
            return Convert.ToBase64String(signature);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sign handshake transcript");
            return null;
        }
    }

    private static byte[] BuildSignatureTranscript(ReadOnlySpan<byte> serverPublicKey, ReadOnlySpan<byte> clientPublicKey)
    {
        var marker = Encoding.ASCII.GetBytes("AEGIS-HANDSHAKE-V1");
        var transcript = new byte[
            marker.Length +
            sizeof(int) + serverPublicKey.Length +
            sizeof(int) + clientPublicKey.Length];

        var offset = 0;
        Buffer.BlockCopy(marker, 0, transcript, offset, marker.Length);
        offset += marker.Length;

        BinaryPrimitives.WriteInt32LittleEndian(transcript.AsSpan(offset, sizeof(int)), serverPublicKey.Length);
        offset += sizeof(int);
        serverPublicKey.CopyTo(transcript.AsSpan(offset, serverPublicKey.Length));
        offset += serverPublicKey.Length;

        BinaryPrimitives.WriteInt32LittleEndian(transcript.AsSpan(offset, sizeof(int)), clientPublicKey.Length);
        offset += sizeof(int);
        clientPublicKey.CopyTo(transcript.AsSpan(offset, clientPublicKey.Length));

        return transcript;
    }
}
