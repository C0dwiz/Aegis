using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
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

namespace Aegis.Server;

public static class Program
{
    public static async Task Main(string[] args)
    {
        try
        {
            var host = CreateHostBuilder(args).Build();
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

    private static async Task InitializeDatabaseAsync(IHost host)
    {
        using var scope = host.Services.CreateScope();
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
            logger.LogWarning(ex,
                "Database migration failed for provider {Provider}. Falling back to EnsureCreated().",
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
                services.Configure<CryptoOptions>(
                    context.Configuration.GetSection(CryptoOptions.SectionName));
                services.Configure<ProtocolSecurityOptions>(
                    context.Configuration.GetSection(ProtocolSecurityOptions.SectionName));
                services.Configure<RateLimitOptions>(
                    context.Configuration.GetSection(RateLimitOptions.SectionName));
                services.Configure<DatabaseOptions>(
                    context.Configuration.GetSection(DatabaseOptions.SectionName));
                services.Configure<LoggingOptions>(
                    context.Configuration.GetSection(LoggingOptions.SectionName));
                services.Configure<AvatarStorageOptions>(
                    context.Configuration.GetSection(AvatarStorageOptions.SectionName));

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
                services.AddScoped<IMessageRepository, MessageRepository>();
                services.AddScoped<IChannelRepository, ChannelRepository>();
                services.AddScoped<IPrivateChatRepository, PrivateChatRepository>();
                services.AddScoped<IGroupRepository, GroupRepository>();
                services.AddScoped<IBotRepository, BotRepository>();
                services.AddScoped<IBotTokenRepository, BotTokenRepository>();
                services.AddScoped<IBotConversationStateRepository, BotConversationStateRepository>();

                // Register services
                services.AddScoped<IUserRegistrationService, UserRegistrationService>();
                services.AddScoped<IUserAuthenticationService, UserAuthenticationService>();
                services.AddScoped<IUserSearchService, UserSearchService>();
                services.AddScoped<IUserProfileService, UserProfileService>();
                services.AddSingleton<IAvatarStorageService, LocalAvatarStorageService>();
                services.AddScoped<IChannelService, ChannelService>();
                services.AddScoped<IGroupService, GroupService>();
                services.AddScoped<IMessageService, MessageService>();
                services.AddScoped<IBotManagementService, BotManagementService>();

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
                services.AddSingleton<RateLimiter>(sp => 
                    new RateLimiter(context.Configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>() ?? new()));
                services.AddSingleton<HealthCheckService>();
                services.AddSingleton<GracefulShutdownManager>();
                services.AddSingleton<IMessageSender, ServerMessageSender>();

                // Register transport with explicit options binding for ctor params
                services.AddSingleton<TcpServer>(sp =>
                {
                    var options = sp.GetRequiredService<IOptions<ServerOptions>>().Value;
                    return new TcpServer(
                        options.Port,
                        options.MaxConnections,
                        options.BufferSize,
                        options.EnableIPv6,
                        options.IdleTimeoutSeconds,
                        options.PartialFrameTimeoutMs,
                        options.MaxIncompleteFrameDrops,
                        options.EnableTransportMasking ? options.TransportMaskingKey : null,
                        sp.GetRequiredService<RateLimiter>(),
                        sp.GetRequiredService<Aegis.Common.Logging.ILogger>());
                });

                // Register handlers as scoped concrete types to resolve only the needed one per message
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
                services.AddScoped<MessageRouter>();

                // Register hosted service
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
    private readonly RateLimiter _rateLimiter;
    private readonly SessionManager _sessionManager;
    private readonly MessageDeduplicator _messageDeduplicator;
    private readonly HealthCheckService _healthCheckService;
    private readonly ProtocolSecurityOptions _protocolSecurityOptions;
    private readonly ILogger<AegisMessengerService> _logger;
    private readonly ServerOptions _serverOptions;

    public AegisMessengerService(
        TcpServer server,
        IServiceScopeFactory scopeFactory,
        Aegis.Crypto.ICryptoProvider crypto,
        RateLimiter rateLimiter,
        SessionManager sessionManager,
        MessageDeduplicator messageDeduplicator,
        HealthCheckService healthCheckService,
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
        _protocolSecurityOptions = protocolSecurityOptions.Value;
        _logger = logger;
        _serverOptions = serverOptions.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting Aegis Messenger Server on port {Port}", _serverOptions.Port);

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
        _sessionManager.CreateSession(context.ConnectionId);
        _healthCheckService.RecordConnectionAccepted();
        _logger.LogInformation("Client {ConnectionId} connected from {RemoteEndPoint}", 
            context.ConnectionId, context.Socket.RemoteEndPoint);
    }

    private void OnClientDisconnected(ConnectionContext context)
    {
        var authenticated = _sessionManager.GetAuthenticatedSession(context.ConnectionId);
        if (authenticated != null)
        {
            _ = PersistOfflinePresenceAsync(authenticated.UserId, context.ConnectionId);
        }

        _rateLimiter.RemoveConnection(context.ConnectionId);
        _messageDeduplicator.ClearConnection(context.ConnectionId);
        _sessionManager.RemoveSession(context.ConnectionId);
        _healthCheckService.RecordConnectionClosed();
        _logger.LogInformation("Client {ConnectionId} disconnected", context.ConnectionId);
    }

    private async Task HealthLoggingLoopAsync(CancellationToken cancellationToken)
    {
        var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                _healthCheckService.LogStatus();
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
            var isAuthFlow = message.Type == MessageType.Auth || message.Type == MessageType.Register;
            if (!isHandshake && !session.HandshakeEstablished)
            {
                _logger.LogWarning("Rejected {MessageType} from connection {ConnectionId} before handshake", message.Type, context.ConnectionId);
                return;
            }

            if (!isHandshake && !isAuthFlow && !_rateLimiter.CanSendMessage(context.ConnectionId))
            {
                _logger.LogWarning("Rate limit exceeded for connection {ConnectionId} on {MessageType}", context.ConnectionId, message.Type);
                return;
            }

            // Verify MAC
            var messageData = data.Slice(0, data.Length - ProtocolConstants.MacSize);
            var receivedMac = data.Slice(data.Length - ProtocolConstants.MacSize, ProtocolConstants.MacSize);

            if (!isHandshake && !session.MacKey.IsEmpty && !_crypto.VerifyMac(messageData.Span, session.MacKey.Span, receivedMac.Span))
            {
                _logger.LogWarning("Invalid MAC for message {SequenceId} from connection {ConnectionId}",
                    message.SequenceId, context.ConnectionId);
                return;
            }

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
                if (!session.HandshakeEstablished || session.SessionKey.IsEmpty)
                {
                    _logger.LogWarning("Rejected encrypted payload from connection {ConnectionId} without established session key", context.ConnectionId);
                    return;
                }

                if (!TryDecryptPayload(message.Payload, session.SessionKey.Span, out var decryptedPayload))
                {
                    _logger.LogWarning("Failed to decrypt payload for message {SequenceId} from connection {ConnectionId}", message.SequenceId, context.ConnectionId);
                    return;
                }

                message.Payload = decryptedPayload;
                message.PayloadLength = (uint)decryptedPayload.Length;
                message.Flags = (byte)(message.Flags & ~(byte)MessageFlags.Encrypted);
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

    private bool TryDecryptPayload(byte[] payload, ReadOnlySpan<byte> key, out byte[] plaintext)
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
            _crypto.Decrypt(ciphertext, key, nonce, plaintextBuffer);
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
}
