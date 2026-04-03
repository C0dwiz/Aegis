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
using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Distributed;

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

public record HandshakeV2Envelope(
    string Stage,
    ClientHelloV2? ClientHello = null,
    ClientFinishV2? ClientFinish = null);

public record HandshakeV2ResponseEnvelope(
    string Stage,
    bool Success,
    string? Message = null,
    ServerHelloV2? ServerHello = null);

public class HandshakeHandler : IMessageHandler
{
    private static readonly ConcurrentDictionary<ulong, PendingV2Handshake> PendingV2Handshakes = new();
    private static readonly ConcurrentDictionary<string, DateTime> LocalNonceReplayCache = new();

    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;
    private readonly Aegis.Crypto.ICryptoProvider _cryptoProvider;
    private readonly ProtocolSecurityOptions _protocolSecurityOptions;
    private readonly IAppCredentialService? _appCredentialService;
    private readonly IDistributedCache? _cache;
    private readonly ILogger<HandshakeHandler> _logger;

    public MessageType Type => MessageType.Handshake;

    public HandshakeHandler(
        SessionManager sessionManager,
        IMessageSender messageSender,
        Aegis.Crypto.ICryptoProvider cryptoProvider,
        IOptions<ProtocolSecurityOptions> protocolSecurityOptions,
        ILogger<HandshakeHandler> logger,
        IAppCredentialService? appCredentialService = null,
        IDistributedCache? cache = null)
    {
        _sessionManager = sessionManager;
        _messageSender = messageSender;
        _cryptoProvider = cryptoProvider;
        _protocolSecurityOptions = protocolSecurityOptions.Value;
        _appCredentialService = appCredentialService;
        _cache = cache;
        _logger = logger;
    }

    public async ValueTask HandleAsync(ConnectionContext context, Message message)
    {
        if (_protocolSecurityOptions.EnableV2Handshake)
        {
            var handled = await TryHandleV2Async(context, message);
            if (handled)
            {
                return;
            }

            if (!_protocolSecurityOptions.AllowLegacyHandshakeFallback)
            {
                await SendHandshakeResponseAsync(context, message.SequenceId, false, null, "V2 handshake required", allowUnsigned: true);
                return;
            }
        }

        await HandleLegacyAsync(context, message);
    }

    private async Task HandleLegacyAsync(ConnectionContext context, Message message)
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

    private async Task<bool> TryHandleV2Async(ConnectionContext context, Message message)
    {
        HandshakeV2Envelope? envelope;
        try
        {
            envelope = PayloadSerializer.Deserialize<HandshakeV2Envelope>(message.Payload);
        }
        catch
        {
            return false;
        }

        if (envelope == null || string.IsNullOrWhiteSpace(envelope.Stage))
        {
            return false;
        }

        switch (envelope.Stage.Trim().ToLowerInvariant())
        {
            case "client_hello_v2":
                return await HandleV2ClientHelloAsync(context, message, envelope.ClientHello);
            case "client_finish_v2":
                return await HandleV2ClientFinishAsync(context, message, envelope.ClientFinish);
            default:
                await SendV2ResponseAsync(context, message.SequenceId, "server_error_v2", false, "Unknown handshake stage", null, allowUnsigned: true);
                return true;
        }
    }

    private async Task<bool> HandleV2ClientHelloAsync(ConnectionContext context, Message message, ClientHelloV2? hello)
    {
        if (hello == null || hello.ClientEphemeralPublicKey == null || hello.ClientEphemeralPublicKey.Length == 0)
        {
            await SendV2ResponseAsync(context, message.SequenceId, "server_error_v2", false, "Invalid client_hello_v2", null, allowUnsigned: true);
            return true;
        }

        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (Math.Abs(nowMs - hello.ClientUnixTimeMs) > _protocolSecurityOptions.V2HandshakeClockSkewMs)
        {
            await SendV2ResponseAsync(context, message.SequenceId, "server_error_v2", false, "Client clock skew too large", null, allowUnsigned: true);
            return true;
        }

        if (!await TryAcceptClientNonceAsync(hello.ApiId, hello.ClientNonce))
        {
            await SendV2ResponseAsync(context, message.SequenceId, "server_error_v2", false, "Replay detected", null, allowUnsigned: true);
            return true;
        }

        if (!await ValidateAppCredentialsAsync(context.ConnectionId, hello.ApiId, hello.AppHash))
        {
            await SendV2ResponseAsync(context, message.SequenceId, "server_error_v2", false, "Invalid app credentials", null, allowUnsigned: true);
            return true;
        }

        using var keyExchange = EcdhKeyExchange.GenerateKeyPair();
        byte[] sharedSecret;
        try
        {
            sharedSecret = keyExchange.ComputeSharedSecret(hello.ClientEphemeralPublicKey);
        }
        catch
        {
            await SendV2ResponseAsync(context, message.SequenceId, "server_error_v2", false, "Invalid client key", null, allowUnsigned: true);
            return true;
        }

        var transcriptHash = SHA256.HashData(message.Payload);
        Span<byte> serverNonce = stackalloc byte[32];
        Span<byte> cookie = stackalloc byte[24];
        RandomNumberGenerator.Fill(serverNonce);
        RandomNumberGenerator.Fill(cookie);

        var keys = V2KeySchedule.DeriveSessionKeys(sharedSecret, hello.ClientNonce, serverNonce, transcriptHash);
        var expectedProof = ComputeFinishProof(keys.AckKey, transcriptHash);

        var pending = new PendingV2Handshake(
            Cookie: cookie.ToArray(),
            ExpiresAtUtc: DateTime.UtcNow.AddMilliseconds(_protocolSecurityOptions.V2HandshakeCookieTtlMs),
            SessionKey: keys.ClientToServerKey,
            ExpectedProof: expectedProof);
        PendingV2Handshakes.AddOrUpdate(context.ConnectionId, pending, (_, _) => pending);

        var signature = SignHandshake(keyExchange.PublicKeyRaw, hello.ClientEphemeralPublicKey);
        if (_protocolSecurityOptions.RequireSignedHandshakeResponses && signature == null)
        {
            PendingV2Handshakes.TryRemove(context.ConnectionId, out _);
            await SendV2ResponseAsync(context, message.SequenceId, "server_error_v2", false, "Handshake signature unavailable", null, allowUnsigned: true);
            return true;
        }

        var serverHello = new ServerHelloV2(
            ServerEphemeralPublicKey: keyExchange.PublicKeyRaw,
            ServerNonce: serverNonce.ToArray(),
            Cookie: cookie.ToArray(),
            ServerUnixTimeMs: nowMs,
            KeyId: nowMs,
            Signature: signature == null ? Array.Empty<byte>() : Convert.FromBase64String(signature));

        await SendV2ResponseAsync(context, message.SequenceId, "server_hello_v2", true, "Continue with client_finish_v2", serverHello, allowUnsigned: true);
        return true;
    }

    private async Task<bool> HandleV2ClientFinishAsync(ConnectionContext context, Message message, ClientFinishV2? finish)
    {
        if (finish == null)
        {
            await SendV2ResponseAsync(context, message.SequenceId, "server_error_v2", false, "Invalid client_finish_v2", null, allowUnsigned: true);
            return true;
        }

        if (!PendingV2Handshakes.TryGetValue(context.ConnectionId, out var pending))
        {
            await SendV2ResponseAsync(context, message.SequenceId, "server_error_v2", false, "Handshake context not found", null, allowUnsigned: true);
            return true;
        }

        if (DateTime.UtcNow > pending.ExpiresAtUtc)
        {
            PendingV2Handshakes.TryRemove(context.ConnectionId, out _);
            await SendV2ResponseAsync(context, message.SequenceId, "server_error_v2", false, "Handshake cookie expired", null, allowUnsigned: true);
            return true;
        }

        if (!FixedEquals(pending.Cookie, finish.Cookie) || !FixedEquals(pending.ExpectedProof, finish.Proof))
        {
            PendingV2Handshakes.TryRemove(context.ConnectionId, out _);
            await SendV2ResponseAsync(context, message.SequenceId, "server_error_v2", false, "Invalid handshake proof", null, allowUnsigned: true);
            return true;
        }

        if (!_sessionManager.EstablishHandshake(context.ConnectionId, pending.SessionKey))
        {
            PendingV2Handshakes.TryRemove(context.ConnectionId, out _);
            await SendV2ResponseAsync(context, message.SequenceId, "server_error_v2", false, "Unable to establish session", null, allowUnsigned: true);
            return true;
        }

        PendingV2Handshakes.TryRemove(context.ConnectionId, out _);
        await SendV2ResponseAsync(context, message.SequenceId, "server_finish_v2", true, "Handshake established", null, allowUnsigned: false);
        _logger.LogInformation("Handshake V2 completed for connection {ConnectionId}", context.ConnectionId);
        return true;
    }

    private async Task SendV2ResponseAsync(
        ConnectionContext context,
        ulong sequenceId,
        string stage,
        bool success,
        string message,
        ServerHelloV2? serverHello,
        bool allowUnsigned)
    {
        var payload = PayloadSerializer.Serialize(new HandshakeV2ResponseEnvelope(
            Stage: stage,
            Success: success,
            Message: message,
            ServerHello: serverHello));

        await _messageSender.SendProtocolMessageAsync(
            context.ConnectionId,
            (ushort)MessageType.Handshake,
            sequenceId,
            payload,
            allowUnsigned);
    }

    private async Task<bool> ValidateAppCredentialsAsync(ulong connectionId, int appId, string appHash)
    {
        if (_protocolSecurityOptions.RequireAppCredentials)
        {
            if (_appCredentialService == null)
            {
                _logger.LogError("RequireAppCredentials is enabled but IAppCredentialService is not registered");
                return false;
            }

            var credential = await _appCredentialService.ValidateCredentialsAsync(appId, appHash);
            if (credential == null)
            {
                _logger.LogWarning("Handshake V2 rejected: invalid AppId={AppId} from {ConnectionId}", appId, connectionId);
                return false;
            }

            return true;
        }

        if (_appCredentialService != null && appId > 0 && !string.IsNullOrWhiteSpace(appHash))
        {
            _ = await _appCredentialService.ValidateCredentialsAsync(appId, appHash);
        }

        return true;
    }

    private async Task<bool> TryAcceptClientNonceAsync(int apiId, byte[] clientNonce)
    {
        if (clientNonce == null || clientNonce.Length < 16)
        {
            return false;
        }

        var nonceHash = Convert.ToHexString(SHA256.HashData(clientNonce)).ToLowerInvariant();
        var key = $"hs:v2:replay:{apiId}:{nonceHash}";

        if (_cache != null)
        {
            var existing = await _cache.GetStringAsync(key);
            if (!string.IsNullOrEmpty(existing))
            {
                return false;
            }

            await _cache.SetStringAsync(
                key,
                "1",
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(_protocolSecurityOptions.V2ReplayWindowSeconds)
                });
            return true;
        }

        var now = DateTime.UtcNow;
        var cutoff = now.AddSeconds(-_protocolSecurityOptions.V2ReplayWindowSeconds);
        foreach (var pair in LocalNonceReplayCache)
        {
            if (pair.Value < cutoff)
            {
                LocalNonceReplayCache.TryRemove(pair.Key, out _);
            }
        }

        return LocalNonceReplayCache.TryAdd(key, now);
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

    private static byte[] ComputeFinishProof(ReadOnlySpan<byte> ackKey, ReadOnlySpan<byte> transcriptHash)
    {
        var material = new byte[transcriptHash.Length + 6];
        transcriptHash.CopyTo(material);
        Encoding.ASCII.GetBytes("finish").CopyTo(material, transcriptHash.Length);

        using var hmac = new HMACSHA256(ackKey.ToArray());
        return hmac.ComputeHash(material);
    }

    private static bool FixedEquals(byte[]? left, byte[]? right)
    {
        if (left == null || right == null || left.Length != right.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(left, right);
    }

    private sealed record PendingV2Handshake(
        byte[] Cookie,
        DateTime ExpiresAtUtc,
        byte[] SessionKey,
        byte[] ExpectedProof);
}
