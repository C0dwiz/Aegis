namespace Aegis.Common.Configuration;

public class ServerOptions
{
    public const string SectionName = "Server";
    
    public int Port { get; set; } = 8888;
    public int MaxConnections { get; set; } = 10000;
    public int BufferSize { get; set; } = 8192;
    public bool EnableIPv6 { get; set; }
    public int IdleTimeoutSeconds { get; set; } = 300;
    public int GracefulShutdownTimeoutSeconds { get; set; } = 30;
}

public class CryptoOptions
{
    public const string SectionName = "Crypto";
    
    public int EncryptionKeySize { get; set; } = 32;
    public int MacKeySize { get; set; } = 32;
    public int NonceSize { get; set; } = 12;
    public int TagSize { get; set; } = 16;
}

public class RateLimitOptions
{
    public const string SectionName = "RateLimit";
    
    public int MaxAuthAttemptsPerMinute { get; set; } = 5;
    public int MaxMessagesPerSecond { get; set; } = 100;
    public int MaxConnectionsPerIP { get; set; } = 10;
}

public class DatabaseOptions
{
    public const string SectionName = "Database";
    
    public string Provider { get; set; } = "InMemory";
    public string ConnectionString { get; set; } = string.Empty;
}

public class LoggingOptions
{
    public const string SectionName = "Logging";
    
    public string MinimumLevel { get; set; } = "Information";
    public bool Console { get; set; } = true;
    public bool File { get; set; } = true;
    public string FilePath { get; set; } = "logs/aegis-{Date}.log";
}
