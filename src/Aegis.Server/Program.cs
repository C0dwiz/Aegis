using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Serilog;
using Aegis.Common;
using Aegis.Common.Configuration;
using Aegis.Common.Logging;
using Aegis.Crypto;
using Aegis.Handlers;
using Aegis.Protocol;
using Aegis.Transport;
using Aegis.Data;
using Aegis.Data.Repositories;
using Aegis.Data.Services;
using Aegis.DomainRules;
using Aegis.Server.Services;

namespace Aegis.Server;

public static class Program
{
    public static async Task Main(string[] args)
    {
        try
        {
            var host = CreateHostBuilder(args).Build();

            if (args.Any(a => string.Equals(a, "--reencrypt-totp-secrets", StringComparison.OrdinalIgnoreCase)))
            {
                await ReencryptLegacyTotpSecretsAsync(host);
                return;
            }

            await InitializeDatabaseAsync(host);
            await host.RunAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal startup error: {ex}");
            Log.Fatal(ex, "Application terminated unexpectedly");
            Environment.ExitCode = 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    private static async Task ReencryptLegacyTotpSecretsAsync(IHost host)
    {
        using var scope = host.Services.CreateScope();
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Aegis.Server.TotpReencryption");

        var twoFactorService = scope.ServiceProvider.GetRequiredService<IUserTwoFactorService>();
        var reencrypted = await twoFactorService.ReencryptLegacySecretsAsync();

        logger.LogInformation("Legacy TOTP secret re-encryption completed. Updated users: {Count}", reencrypted);
    }

    private static async Task InitializeDatabaseAsync(IHost host)
    {
        using var scope = host.Services.CreateScope();
        var hostEnvironment = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Aegis.Server.DatabaseInitialization");
        var databaseOptions = scope.ServiceProvider
            .GetRequiredService<IOptions<DatabaseOptions>>()
            .Value;
        var dbContext = scope.ServiceProvider.GetRequiredService<AegisDbContext>();

        logger.LogInformation(
            "Applying database migrations using provider {Provider}",
            databaseOptions.Provider);

        try
        {
            await dbContext.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            if (hostEnvironment.IsProduction())
            {
                logger.LogError(ex,
                    "Database migration failed in production for provider {Provider}. Startup aborted.",
                    databaseOptions.Provider);
                throw;
            }

            logger.LogWarning(ex,
                "Database migration failed for provider {Provider}. Falling back to EnsureCreated() in non-production environment.",
                databaseOptions.Provider);
            await dbContext.Database.EnsureCreatedAsync();
        }

        var botManagementService = scope.ServiceProvider.GetRequiredService<IBotManagementService>();
        await botManagementService.EnsureBotFatherExistsAsync();

        logger.LogInformation("Database schema is ready");
    }

    private static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                var configBasePath = AppContext.BaseDirectory;
                config
                    .SetBasePath(configBasePath)
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json",
                        optional: true, reloadOnChange: true)
                    .AddEnvironmentVariables("AEGIS_")
                    .AddCommandLine(args);
            })
            .ConfigureServices((context, services) =>
            {
                StartupValidation.ValidateServerConfiguration(context.Configuration, context.HostingEnvironment);

                // Configure options
                services.Configure<ServerOptions>(
                    context.Configuration.GetSection(ServerOptions.SectionName));
                services.Configure<TlsOptions>(
                    context.Configuration.GetSection(TlsOptions.SectionName));
                services.Configure<CryptoOptions>(
                    context.Configuration.GetSection(CryptoOptions.SectionName));
                services.Configure<ProtocolSecurityOptions>(
                    context.Configuration.GetSection(ProtocolSecurityOptions.SectionName));
                services.PostConfigure<ProtocolSecurityOptions>(options =>
                {
                    if (context.HostingEnvironment.IsProduction() || context.HostingEnvironment.IsStaging())
                    {
                        options.EnableV2Handshake = true;
                        options.AllowLegacyHandshakeFallback = false;
                    }
                });
                services.Configure<RateLimitOptions>(
                    context.Configuration.GetSection(RateLimitOptions.SectionName));
                services.Configure<DatabaseOptions>(
                    context.Configuration.GetSection(DatabaseOptions.SectionName));
                services.Configure<LoggingOptions>(
                    context.Configuration.GetSection(LoggingOptions.SectionName));
                services.Configure<AvatarStorageOptions>(
                    context.Configuration.GetSection(AvatarStorageOptions.SectionName));
                services.Configure<MinioStorageOptions>(
                    context.Configuration.GetSection(MinioStorageOptions.SectionName));
                services.Configure<ElasticsearchOptions>(
                    context.Configuration.GetSection(ElasticsearchOptions.SectionName));
                services.Configure<IdGeneratorOptions>(
                    context.Configuration.GetSection(IdGeneratorOptions.SectionName));
                services.Configure<ChannelService.ChannelLinkOptions>(
                    context.Configuration.GetSection(ChannelService.ChannelLinkOptions.SectionName));

                // Register distributed ID generator as Scoped to ensure proper initialization
                // with node ID from configuration
                services.AddScoped<Aegis.Data.Utils.FastIdGenerator>(serviceProvider =>
                {
                    var idGeneratorOptions = serviceProvider
                        .GetRequiredService<IOptions<IdGeneratorOptions>>()
                        .Value;
                    return new Aegis.Data.Utils.FastIdGenerator(idGeneratorOptions.NodeId);
                });
                services.AddHttpClient<ElasticsearchUserSearchIndexService>();
                services.AddScoped<IUserSearchIndexService>(serviceProvider =>
                {
                    var searchOptions = serviceProvider
                        .GetRequiredService<IOptions<ElasticsearchOptions>>()
                        .Value;

                    if (!searchOptions.Enabled)
                    {
                        return new NoOpUserSearchIndexService();
                    }

                    return serviceProvider.GetRequiredService<ElasticsearchUserSearchIndexService>();
                });

                var redisConnectionString = context.Configuration["Redis:ConnectionString"] ?? "localhost:6379";
                services.AddStackExchangeRedisCache(options =>
                {
                    options.Configuration = redisConnectionString;
                    options.InstanceName = "aegis:";
                });

                // Register database
                services.AddDbContextPool<AegisDbContext>((serviceProvider, options) =>
                {
                    var databaseOptions = serviceProvider
                        .GetRequiredService<IOptions<DatabaseOptions>>()
                        .Value;
                    var connectionString = string.IsNullOrWhiteSpace(databaseOptions.ConnectionString)
                        ? "Host=localhost;Port=5432;Database=aegis;Username=aegis;Password=aegis"
                        : databaseOptions.ConnectionString;

                    options.UseNpgsql(connectionString);
                });

                // Register repositories
                services.AddScoped<IUserRepository, UserRepository>();
                services.AddScoped<IUserAvatarRepository, UserAvatarRepository>();
                services.AddScoped<ISessionRepository, SessionRepository>();
                // ZoneTree holds open file handles - must be Singleton to avoid
                // "database already in use" errors and potential data corruption
                // under concurrent requests with Scoped lifetime.
                services.AddSingleton<IMessageRepository>(serviceProvider =>
                {
                    var databaseOptions = serviceProvider
                        .GetRequiredService<IOptions<DatabaseOptions>>()
                        .Value;
                    var zoneTreePath = string.IsNullOrWhiteSpace(databaseOptions.ZoneTreePath)
                        ? "zonetree-messages-db"
                        : databaseOptions.ZoneTreePath;
                    return new ZoneTreeMessageRepository(zoneTreePath);
                });
                services.AddScoped<IMessageDeliveryRepository, MessageDeliveryRepository>();
                services.AddScoped<IChannelRepository, ChannelRepository>();
                services.AddScoped<IPrivateChatRepository, PrivateChatRepository>();
                services.AddScoped<IGroupRepository, GroupRepository>();
                services.AddScoped<IBotRepository, BotRepository>();
                services.AddScoped<IBotTokenRepository, BotTokenRepository>();
                services.AddScoped<IBotConversationStateRepository, BotConversationStateRepository>();
                services.AddScoped<IAppCredentialRepository, AppCredentialRepository>();
                services.AddScoped<IReactionRepository, ReactionRepository>();
                services.AddScoped<ISignalChainStateRepository, SignalChainStateRepository>();

                // Register services
                services.AddScoped<IUserRegistrationService, UserRegistrationService>();
                services.AddScoped<IUserAuthenticationService, UserAuthenticationService>();
                services.AddScoped<IUserTwoFactorService, UserTwoFactorService>();
                services.AddScoped<IUserSearchService, UserSearchService>();
                services.AddScoped<IUserProfileService, UserProfileService>();
                services.AddScoped<IAppCredentialService, AppCredentialService>();
                services.AddSingleton<IAvatarStorageService>(serviceProvider =>
                {
                    var avatarOptions = serviceProvider
                        .GetRequiredService<IOptions<AvatarStorageOptions>>()
                        .Value;
                    var minioOptions = serviceProvider
                        .GetRequiredService<IOptions<MinioStorageOptions>>()
                        .Value;

                    var useMinio = string.Equals(avatarOptions.Provider, "MinIO", StringComparison.OrdinalIgnoreCase)
                        || minioOptions.Enabled;

                    if (useMinio)
                    {
                        return ActivatorUtilities.CreateInstance<MinioAvatarStorageService>(serviceProvider);
                    }

                    return ActivatorUtilities.CreateInstance<LocalAvatarStorageService>(serviceProvider);
                });
                services.AddScoped<IChannelService, ChannelService>();
                services.AddScoped<IGroupService, GroupService>();
                services.AddScoped<IMessageService, MessageService>();
                services.AddScoped<IMessageDeliveryService, MessageDeliveryService>();
                services.AddScoped<IBotManagementService, BotManagementService>();
                services.AddSingleton<IMessageDomainRules, MessageDomainRules>();
                services.AddSingleton<DomainRulesAdapter>();
                services.AddSingleton<IIdGenerator, IdGenerator>();

                // Register core services (singletons - no DB dependencies)
                services.AddSingleton<Aegis.Common.Logging.ILogger>(_ =>
                    new Aegis.Common.Logging.SerilogLogger(Log.Logger));
                services.AddSingleton<AegisCryptoProvider>();
                services.AddSingleton<Aegis.Crypto.ICryptoProvider>(sp => sp.GetRequiredService<AegisCryptoProvider>());
                services.AddSingleton<Aegis.Common.ICryptoProvider>(sp =>
                    new CommonCryptoProviderAdapter(sp.GetRequiredService<AegisCryptoProvider>()));
                services.AddSingleton<ISessionCryptoProvider>(sp => sp.GetRequiredService<AegisCryptoProvider>());
                services.AddSingleton<SessionManager>();
                services.AddSingleton<UserPresenceResolver>();
                services.AddSingleton<IAntiSpamClient, AntiSpamClient>();
                services.AddSingleton<AcknowledgmentManager>();
                services.AddSingleton<MessageDeduplicator>();
                services.AddSingleton<IRateLimiter>(sp =>
                {
                    var options = context.Configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>() ?? new();
                    var redis = context.Configuration["Redis:ConnectionString"];
                    var logger = sp.GetRequiredService<ILogger<RedisRateLimiter>>();
                    return new RedisRateLimiter(options, redis, logger);
                });
                services.AddSingleton<HealthCheckService>();
                services.AddSingleton<GracefulShutdownManager>();
                services.AddSingleton<IMessageSender, ServerMessageSender>();
                services.AddSingleton<TypingIndicatorStore>();
                services.AddSingleton<FileTransferStore>();
                services.AddSingleton<IFileDownloadRateLimiter, FileDownloadRateLimiter>();
                services.AddSingleton<ConnectionBalancer>();

                // Register transport with explicit options binding for ctor params
                services.AddSingleton<TcpServer>(sp =>
                {
                    var options = sp.GetRequiredService<IOptions<ServerOptions>>().Value;
                    var tlsCfg   = sp.GetRequiredService<IOptions<TlsOptions>>().Value;

                    SslServerAuthenticationOptions? sslOptions = null;
                    if (tlsCfg.Enabled)
                    {
                        if (string.IsNullOrWhiteSpace(tlsCfg.CertificatePath))
                            throw new InvalidOperationException(
                                "Tls:Enabled is true but Tls:CertificatePath is empty.");

                        var cert = X509CertificateLoader.LoadPkcs12FromFile(
                            tlsCfg.CertificatePath,
                            tlsCfg.CertificatePassword,
                            X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);

                        sslOptions = new SslServerAuthenticationOptions
                        {
                            ServerCertificate          = cert,
                            ClientCertificateRequired  = false,
                            EnabledSslProtocols        = SslProtocols.Tls12 | SslProtocols.Tls13,
                            CertificateRevocationCheckMode =
                                X509RevocationMode.NoCheck,
                        };
                    }

                    return new TcpServer(
                        options.Port,
                        options.MaxConnections,
                        options.BufferSize,
                        options.EnableIPv6,
                        options.IdleTimeoutSeconds,
                        options.PartialFrameTimeoutMs,
                        options.MaxIncompleteFrameDrops,
                        options.EnableTransportMasking ? options.TransportMaskingKey : null,
                        sp.GetRequiredService<IRateLimiter>(),
                        connectionAdmission: _ => sp.GetRequiredService<ConnectionBalancer>().ShouldAcceptLocalConnection(),
                        tlsOptions: sslOptions,
                        logger: sp.GetRequiredService<Aegis.Common.Logging.ILogger>());
                });

                // Register handlers as scoped concrete types to resolve only the needed one per message
                                services.AddScoped<MessageReadReceiptHandler>();
                services.AddScoped<HandshakeHandler>();
                services.AddScoped<AuthHandler>();
                services.AddScoped<PingHandler>();
                services.AddScoped<MessageHandler>();
                services.AddScoped<AckHandler>();
                services.AddScoped<NackHandler>();
                services.AddScoped<RetransmitRequestHandler>();
                services.AddScoped<RegistrationHandler>();
                services.AddScoped<UserPresenceHandler>();
                services.AddScoped<UserSearchHandler>();
                services.AddScoped<ChannelMessageHandler>();
                services.AddScoped<ChannelCreateHandler>();
                services.AddScoped<ChannelJoinHandler>();
                services.AddScoped<PrivateChatMessageHandler>();
                services.AddScoped<ChatListHandler>();
                services.AddScoped<PrivateChatHistoryHandler>();
                services.AddScoped<ChannelHistoryHandler>();
                services.AddScoped<ProfileUpdateHandler>();
                services.AddScoped<ProfileGetHandler>();
                services.AddScoped<ProfileAvatarAddHandler>();
                services.AddScoped<ProfileAvatarListHandler>();
                services.AddScoped<ProfileAvatarDeleteHandler>();
                services.AddScoped<ProfileAvatarSetPrimaryHandler>();
                services.AddScoped<ChannelLinkUpdateHandler>();
                services.AddScoped<ChannelLinkGetHandler>();
                services.AddScoped<ChannelResolveHandler>();
                services.AddScoped<ChannelJoinByLinkHandler>();
                services.AddScoped<MessageEditHandler>();
                services.AddScoped<MessageDeleteHandler>();
                services.AddScoped<ChannelEditHandler>();
                services.AddScoped<GroupCreateHandler>();
                services.AddScoped<GroupEditHandler>();
                services.AddScoped<GroupMessageSendHandler>();
                services.AddScoped<MemberRoleUpdateHandler>();
                services.AddScoped<MemberPermissionUpdateHandler>();
                services.AddScoped<MessageReadReceiptHandler>();
                services.AddScoped<MessageDeliveryReceiptHandler>();
                // SERVER-002
                services.AddScoped<GroupHistoryHandler>();
                // SERVER-003
                services.AddScoped<ChannelMembersHandler>();
                services.AddScoped<GroupMembersHandler>();
                // SERVER-004
                services.AddScoped<ChannelLeaveHandler>();
                services.AddScoped<GroupLeaveHandler>();
                // SERVER-005
                services.AddScoped<MessageReactHandler>();
                services.AddScoped<MessagePinHandler>();
                // SERVER-006
                services.AddScoped<RoomSettingsGetHandler>();
                services.AddScoped<RoomSettingsUpdateHandler>();
                // TODO-012/017/018
                services.AddScoped<UserTypingHandler>();
                services.AddScoped<FileTransferHandler>();
                services.AddScoped<MessageRouter>();

                // Register background services
                services.AddHostedService<Aegis.Server.Services.SessionCleanupBackgroundService>();
                services.AddHostedService<Aegis.Server.Services.ProtocolSecurityCleanupBackgroundService>();
                services.AddHostedService<Aegis.Server.Services.OfflineMessageService>();
                services.AddHostedService<AegisMessengerService>();
            })
            .UseSerilog((context, loggerConfig) =>
            {
                var loggingOptions = context.Configuration
                    .GetSection(LoggingOptions.SectionName)
                    .Get<LoggingOptions>() ?? new();

                loggerConfig
                    .MinimumLevel.Information()
                    .Enrich.FromLogContext()
                    .Enrich.WithProperty("Application", "AegisMessenger");

                if (loggingOptions.Console)
                {
                    loggerConfig.WriteTo.Console(
                        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] {Message:lj}{NewLine}{Exception}");
                }

                if (loggingOptions.File)
                {
                    loggerConfig.WriteTo.File(
                        loggingOptions.FilePath,
                        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] {Message:lj}{NewLine}{Exception}");
                }
            });
}

/// <summary>
/// Main hosted service for Aegis Messenger Server
/// </summary>
public class AegisMessengerService : BackgroundService
{
    private readonly TcpServer _server;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Aegis.Crypto.ICryptoProvider _crypto;
    private readonly IRateLimiter _rateLimiter;
    private readonly SessionManager _sessionManager;
    private readonly MessageDeduplicator _messageDeduplicator;
    private readonly HealthCheckService _healthCheckService;
    private readonly ConnectionBalancer _connectionBalancer;
    private readonly ProtocolSecurityOptions _protocolSecurityOptions;
    private readonly ILogger<AegisMessengerService> _logger;
    private readonly ServerOptions _serverOptions;

    public AegisMessengerService(
        TcpServer server,
        IServiceScopeFactory scopeFactory,
        Aegis.Crypto.ICryptoProvider crypto,
        IRateLimiter rateLimiter,
        SessionManager sessionManager,
        MessageDeduplicator messageDeduplicator,
        HealthCheckService healthCheckService,
        ConnectionBalancer connectionBalancer,
        Microsoft.Extensions.Options.IOptions<ProtocolSecurityOptions> protocolSecurityOptions,
        ILogger<AegisMessengerService> logger,
        Microsoft.Extensions.Options.IOptions<ServerOptions> serverOptions)
    {
        _server = server;
        _scopeFactory = scopeFactory;
        _crypto = crypto;
        _rateLimiter = rateLimiter;
        _sessionManager = sessionManager;
        _messageDeduplicator = messageDeduplicator;
        _healthCheckService = healthCheckService;
        _connectionBalancer = connectionBalancer;
        _protocolSecurityOptions = protocolSecurityOptions.Value;
        _logger = logger;
        _serverOptions = serverOptions.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting Aegis Messenger Server on port {Port}", _serverOptions.Port);

        var localNodeId = $"{Environment.MachineName}:{_serverOptions.Port}";
        _connectionBalancer.ConfigureLocalNode(localNodeId);

        _server.OnClientConnected += OnClientConnected;
        _server.OnClientDisconnected += OnClientDisconnected;
        _server.OnMessageReceived += async (context, data) =>
            await ProcessMessageAsync(context, data, stoppingToken);

        _ = Task.Run(() => HealthLoggingLoopAsync(stoppingToken), stoppingToken);

        try
        {
            await _server.StartAsync(_serverOptions.Port);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error starting server");
            throw;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Shutting down Aegis Messenger Server gracefully");
        await _server.StopAsync();
        await base.StopAsync(cancellationToken);
    }

    private void OnClientConnected(ConnectionContext context)
    {
        var ipAddress = (context.Socket.RemoteEndPoint as System.Net.IPEndPoint)?.Address.ToString();
        if (!string.IsNullOrWhiteSpace(ipAddress))
        {
            _rateLimiter.RegisterConnection(context.ConnectionId, ipAddress);
        }
        _connectionBalancer.RecordLocalConnectionAccepted();
        _healthCheckService.RecordConnectionAccepted();
    }

    private void OnClientDisconnected(ConnectionContext context)
    {
        var authenticated = _sessionManager.GetAuthenticatedSession(context.ConnectionId);
        if (authenticated != null)
        {
            _ = PersistOfflinePresenceAsync(authenticated.UserId, context.ConnectionId);
        }

        _rateLimiter.RemoveConnection(context.ConnectionId);
        _connectionBalancer.ReleaseLocalConnection();
        _messageDeduplicator.ClearConnection(context.ConnectionId);
        _sessionManager.RemoveSession(context.ConnectionId);
        _healthCheckService.RecordConnectionClosed();
    }

    private async Task HealthLoggingLoopAsync(CancellationToken cancellationToken)
    {
        var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                _healthCheckService.LogStatus();
                _connectionBalancer.UpdateLocalHealth(
                    isHealthy: true,
                    cpuLoadPercent: GetCpuLoadPercent(),
                    memoryLoadPercent: GetMemoryLoadPercent());
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown path.
        }
        finally
        {
            timer.Dispose();
        }
    }

    private async Task PersistOfflinePresenceAsync(ulong userId, ulong connectionId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var sessionRepository = scope.ServiceProvider.GetRequiredService<ISessionRepository>();

            var user = await userRepository.GetByIdAsync(userId);
            if (user != null)
            {
                user.LastSeenAt = DateTime.UtcNow;
                user.UpdatedAt = DateTime.UtcNow;
                await userRepository.UpdateAsync(user);
            }

            var dbSession = await sessionRepository.GetByConnectionIdAsync(connectionId.ToString());
            if (dbSession != null)
            {
                dbSession.LastActivityAt = DateTime.UtcNow;
                dbSession.IsActive = false;
                await sessionRepository.UpdateAsync(dbSession);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist offline presence for user {UserId}", userId);
        }
    }

    private async Task ProcessMessageAsync(
        ConnectionContext context,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken)
    {
        try
        {
            // Decode message
            var message = MessageEncoder.Decode(data.Span);

            var session = _sessionManager.GetSession(context.ConnectionId) ??
                _sessionManager.CreateSession(context.ConnectionId);

            var isHandshake = message.Type == MessageType.Handshake;
            var isRegister = message.Type == MessageType.Register;
            if (!isHandshake && !isRegister && !session.HandshakeEstablished)
            {
                _logger.LogWarning("Rejected {MessageType} from connection {ConnectionId} before handshake", message.Type, context.ConnectionId);
                return;
            }

            if (!isHandshake && !_rateLimiter.CanSendMessage(context.ConnectionId))
            {
                _logger.LogWarning("Rate limit exceeded for connection {ConnectionId} on {MessageType}", context.ConnectionId, message.Type);
                return;
            }

            // Verify MAC
            var encryptedPayload = (message.Flags & (byte)MessageFlags.Encrypted) != 0;
            if (!isHandshake && session.HandshakeEstablished && _protocolSecurityOptions.RequireEncryptedPayloadAfterHandshake && !encryptedPayload)
            {
                _logger.LogWarning(
                    "Rejected unencrypted {MessageType} from connection {ConnectionId} under strict payload encryption policy",
                    message.Type,
                    context.ConnectionId);
                return;
            }

            if (encryptedPayload)
            {
                if (!session.HandshakeEstablished || session.SessionKey.IsEmpty || session.SessionKey.Length != 32)
                {
                    _logger.LogWarning("Rejected encrypted payload from connection {ConnectionId} without established session key", context.ConnectionId);
                    return;
                }

                // Reconstruct the header bytes to use as AAD for AES-GCM.
                if (message.Payload.Length < 28)
                {
                    _logger.LogWarning("Rejected encrypted payload from connection {ConnectionId} due to invalid ciphertext size {CiphertextSize}", context.ConnectionId, message.Payload.Length);
                    return;
                }

                var headerBytes = data.Slice(0, ProtocolConstants.HeaderSize);

                if (!TryDecryptPayload(message.Payload, session.SessionKey.Span, headerBytes.Span, out var decryptedPayload))
                {
                    _logger.LogWarning("Failed to decrypt payload for message {SequenceId} from connection {ConnectionId}", message.SequenceId, context.ConnectionId);
                    return;
                }

                message.Payload = decryptedPayload;
                message.PayloadLength = (uint)decryptedPayload.Length;
                message.Flags = (byte)(message.Flags & ~(byte)MessageFlags.Encrypted);
            }

            var compressedPayload = (message.Flags & (byte)MessageFlags.Compressed) != 0;
            if (compressedPayload)
            {
                if (!TryDecompressBrotli(message.Payload, out var decompressed))
                {
                    _logger.LogWarning("Failed to decompress payload for message {SequenceId} from connection {ConnectionId}", message.SequenceId, context.ConnectionId);
                    return;
                }
                message.Payload = decompressed;
                message.PayloadLength = (uint)decompressed.Length;
                message.Flags = (byte)(message.Flags & ~(byte)MessageFlags.Compressed);
            }

            if (!isHandshake && !_messageDeduplicator.TryAcceptSequence(context.ConnectionId, message.SequenceId, out var replayReason))
            {
                _logger.LogWarning(
                    "Rejected replay or stale message {SequenceId} from connection {ConnectionId}: {Reason}",
                    message.SequenceId,
                    context.ConnectionId,
                    replayReason);
                return;
            }

            _sessionManager.UpdateActivity(context.ConnectionId);
            _healthCheckService.RecordMessageProcessed();

            // Create a scope per message so scoped services (DB, repos, handlers) are properly managed
            using var scope = _scopeFactory.CreateScope();
            var router = scope.ServiceProvider.GetRequiredService<MessageRouter>();
            await router.RouteAsync(context, message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message from connection {ConnectionId}",
                context.ConnectionId);
        }
    }

    private bool TryDecryptPayload(byte[] payload, ReadOnlySpan<byte> key, ReadOnlySpan<byte> aad, out byte[] plaintext)
    {
        plaintext = Array.Empty<byte>();

        const int nonceSize = 12;
        const int tagSize = 16;
        if (payload.Length < nonceSize + tagSize)
        {
            return false;
        }

        var nonce = payload.AsSpan(0, nonceSize);
        var ciphertext = payload.AsSpan(nonceSize);
        var plaintextBuffer = new byte[ciphertext.Length - tagSize];

        try
        {
            _crypto.Decrypt(ciphertext, key, nonce, plaintextBuffer, aad);
            plaintext = plaintextBuffer;
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryDecompressBrotli(byte[] data, out byte[] decompressed)
    {
        decompressed = Array.Empty<byte>();
        try
        {
            using var input = new System.IO.MemoryStream(data);
            using var brotli = new System.IO.Compression.BrotliStream(input, System.IO.Compression.CompressionMode.Decompress);
            using var output = new System.IO.MemoryStream();
            brotli.CopyTo(output);
            decompressed = output.ToArray();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static double GetMemoryLoadPercent()
    {
        var gcInfo = GC.GetGCMemoryInfo();
        var totalAvailable = gcInfo.TotalAvailableMemoryBytes;
        if (totalAvailable <= 0)
        {
            return 0;
        }

        var used = Process.GetCurrentProcess().WorkingSet64;
        return Math.Clamp(used / (double)totalAvailable * 100.0, 0, 100);
    }

    private static double GetCpuLoadPercent()
    {
        // Keep a stable baseline value for admission heuristics without introducing expensive samplers.
        return 0;
    }
}
