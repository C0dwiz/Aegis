namespace Aegis.Common.Configuration;

public class ServerOptions
{
    public const string SectionName = "Server";

    public int Port { get; set; } = 8888;
    public int MaxConnections { get; set; } = 10000;
    public int BufferSize { get; set; } = 8192;
    public bool EnableIPv6 { get; set; }
    public int IdleTimeoutSeconds { get; set; } = 300;
    public int PartialFrameTimeoutMs { get; set; } = 300;
    public int MaxIncompleteFrameDrops { get; set; } = 3;
    public bool EnableTransportMasking { get; set; } = false;
    public string TransportMaskingKey { get; set; } = string.Empty;
    public int GracefulShutdownTimeoutSeconds { get; set; } = 30;

    /// <summary>Port for the Prometheus /metrics HTTP endpoint (0 = use default 9091).</summary>
    public int MetricsPort { get; set; } = 9091;
}

public class CryptoOptions
{
    public const string SectionName = "Crypto";

    public int EncryptionKeySize { get; set; } = 32;
    public int MacKeySize { get; set; } = 32;
    public int NonceSize { get; set; } = 12;
    public int TagSize { get; set; } = 16;
}

public class ProtocolSecurityOptions
{
    public const string SectionName = "ProtocolSecurity";

    // When true, non-handshake and non-auth flow messages must carry encrypted payloads after handshake.
    public bool RequireEncryptedPayloadAfterHandshake { get; set; } = false;

    // When true, server encrypts protocol payloads for established sessions.
    public bool EncryptServerPayloadsAfterHandshake { get; set; } = true;

    // When true, server must sign handshake responses using HandshakeSigningPrivateKeyBase64.
    public bool RequireSignedHandshakeResponses { get; set; } = false;

    // Base64-encoded PKCS#8 ECDSA P-256 private key for handshake response signatures.
    public string HandshakeSigningPrivateKeyBase64 { get; set; } = string.Empty;

    /// <summary>
    /// When true, every handshake must supply a valid AppId + AppHash from a registered
    /// application credential.  Set to false during migration to allow legacy clients
    /// without credentials to still connect.
    /// </summary>
    public bool RequireAppCredentials { get; set; } = false;

    // Enables staged V2 runtime handshake (client_hello_v2/server_hello_v2/client_finish_v2).
    public bool EnableV2Handshake { get; set; } = true;

    // Allow fallback to legacy handshake format if V2 envelope is not present.
    public bool AllowLegacyHandshakeFallback { get; set; } = true;

    // Allowed client clock skew for V2 handshake freshness validation.
    public long V2HandshakeClockSkewMs { get; set; } = 90_000;

    // Cookie lifetime for V2 anti-DoS handshake stage.
    public long V2HandshakeCookieTtlMs { get; set; } = 60_000;

    // Replay cache window for V2 client nonce deduplication.
    public int V2ReplayWindowSeconds { get; set; } = 120;
}

public class RateLimitOptions
{
    public const string SectionName = "RateLimit";

    public int MaxAuthAttemptsPerMinute { get; set; } = 5;
    public int MaxMessagesPerSecond { get; set; } = 100;
    public int MaxConnectionsPerIP { get; set; } = 10;
}

public class TlsOptions
{
    public const string SectionName = "Tls";

    /// <summary>Enable TLS on the TCP transport layer.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Path to a PFX/PKCS#12 certificate file.</summary>
    public string CertificatePath { get; set; } = string.Empty;

    /// <summary>
    /// Password for the PFX file.  Prefer supplying this via the
    /// AEGIS_TLS__CERTIFICATEPASSWORD environment variable so it is not
    /// committed to config files.
    /// </summary>
    public string CertificatePassword { get; set; } = string.Empty;
}

public class DatabaseOptions
{
    public const string SectionName = "Database";

    public string Provider { get; set; } = "PostgreSQL";
    public string ConnectionString { get; set; } = string.Empty;
    public string ZoneTreePath { get; set; } = "zonetree-messages-db";
}

public class LoggingOptions
{
    public const string SectionName = "AegisLogging";

    public string MinimumLevel { get; set; } = "Information";
    public bool Console { get; set; } = true;
    public bool File { get; set; } = true;
    public string FilePath { get; set; } = "logs/aegis-{Date}.log";
}

public class OfflineMessageOptions
{
    public const string SectionName = "OfflineMessage";

    /// <summary>
    /// How long an undelivered message is kept before being silently dropped.
    /// Default: 7 days. Set to 0 to disable TTL (keep forever).
    /// </summary>
    public int MessageTtlSeconds { get; set; } = 604_800; // 7 days

    /// <summary>Maximum undelivered messages queued per user before old ones are dropped.</summary>
    public int MaxQueuedPerUser { get; set; } = 1000;

    /// <summary>How often (in seconds) the delivery loop wakes up to push pending messages.</summary>
    public int DeliveryIntervalSeconds { get; set; } = 5;
}

public class SessionOptions
{
    public const string SectionName = "Sessions";

    /// <summary>
    /// Maximum number of concurrent active sessions (devices) per user.
    /// When exceeded, the oldest session is revoked automatically.
    /// 0 = unlimited. Default: 50.
    /// </summary>
    public int MaxSessionsPerUser { get; set; } = 50;
}
