using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
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
            await host.RunAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    private static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                config
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", 
                        optional: true, reloadOnChange: true)
                    .AddEnvironmentVariables("AEGIS_")
                    .AddCommandLine(args);
            })
            .ConfigureServices((context, services) =>
            {
                // Configure options
                services.Configure<ServerOptions>(
                    context.Configuration.GetSection(ServerOptions.SectionName));
                services.Configure<CryptoOptions>(
                    context.Configuration.GetSection(CryptoOptions.SectionName));
                services.Configure<RateLimitOptions>(
                    context.Configuration.GetSection(RateLimitOptions.SectionName));
                services.Configure<DatabaseOptions>(
                    context.Configuration.GetSection(DatabaseOptions.SectionName));
                services.Configure<LoggingOptions>(
                    context.Configuration.GetSection(LoggingOptions.SectionName));

                // Register database
                services.AddDbContext<AegisDbContext>(options =>
                    options.UseSqlite(context.Configuration.GetConnectionString("DefaultConnection") ?? 
                                     "Data Source=aegis.db"));

                // Register repositories
                services.AddScoped<IUserRepository, UserRepository>();
                services.AddScoped<ISessionRepository, SessionRepository>();
                services.AddScoped<IMessageRepository, MessageRepository>();
                services.AddScoped<IChannelRepository, ChannelRepository>();
                services.AddScoped<IPrivateChatRepository, PrivateChatRepository>();

                // Register services
                services.AddScoped<IUserRegistrationService, UserRegistrationService>();
                services.AddScoped<IUserAuthenticationService, UserAuthenticationService>();
                services.AddScoped<IUserSearchService, UserSearchService>();

                // Register core services
                services.AddSingleton<Aegis.Crypto.ICryptoProvider, AegisCryptoProvider>();
                services.AddSingleton<Aegis.Common.ICryptoProvider, CommonCryptoProviderAdapter>();
                services.AddSingleton<ISessionCryptoProvider>(sp => 
                    sp.GetRequiredService<Aegis.Crypto.ICryptoProvider>() as AegisCryptoProvider 
                    ?? throw new InvalidOperationException("AegisCryptoProvider must implement ISessionCryptoProvider"));
                services.AddSingleton<SessionManager>();
                services.AddSingleton<IAntiSpamClient, AntiSpamClient>();
                services.AddSingleton<AcknowledgmentManager>();
                services.AddSingleton<MessageDeduplicator>();
                services.AddSingleton<RateLimiter>(sp => 
                    new RateLimiter(context.Configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>() ?? new()));
                services.AddSingleton<HealthCheckService>();
                services.AddSingleton<GracefulShutdownManager>();

                // Register transport
                services.AddSingleton<TcpServer>();

                // Register handlers
                services.AddSingleton<IMessageHandler, AuthHandler>();
                services.AddSingleton<IMessageHandler, PingHandler>();
                services.AddSingleton<IMessageHandler, MessageHandler>();
                services.AddSingleton<IMessageHandler, AckHandler>();
                services.AddSingleton<IMessageHandler, NackHandler>();
                services.AddSingleton<IMessageHandler, RetransmitRequestHandler>();
                services.AddSingleton<IMessageHandler, RegistrationHandler>();
                services.AddSingleton<IMessageHandler, UserSearchHandler>();
                services.AddSingleton<IMessageHandler, ChannelMessageHandler>();
                services.AddSingleton<IMessageHandler, ChannelCreateHandler>();
                services.AddSingleton<IMessageHandler, PrivateChatMessageHandler>();
                services.AddSingleton<MessageRouter>();
                services.AddSingleton<IMessageSender, ServerMessageSender>();

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
    private readonly MessageRouter _router;
    private readonly Aegis.Crypto.ICryptoProvider _crypto;
    private readonly SessionManager _sessionManager;
    private readonly ILogger<AegisMessengerService> _logger;
    private readonly ServerOptions _serverOptions;

    public AegisMessengerService(
        TcpServer server,
        MessageRouter router,
        Aegis.Crypto.ICryptoProvider crypto,
        SessionManager sessionManager,
        ILogger<AegisMessengerService> logger,
        Microsoft.Extensions.Options.IOptions<ServerOptions> serverOptions)
    {
        _server = server;
        _router = router;
        _crypto = crypto;
        _sessionManager = sessionManager;
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
        _logger.LogInformation("Client {ConnectionId} connected from {RemoteEndPoint}", 
            context.ConnectionId, context.Socket.RemoteEndPoint);
    }

    private void OnClientDisconnected(ConnectionContext context)
    {
        _sessionManager.RemoveSession(context.ConnectionId);
        _logger.LogInformation("Client {ConnectionId} disconnected", context.ConnectionId);
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

            // Verify MAC
            var messageData = data.Slice(0, data.Length - ProtocolConstants.MacSize);
            var receivedMac = data.Slice(data.Length - ProtocolConstants.MacSize, ProtocolConstants.MacSize);

            var session = _sessionManager.GetSession(context.ConnectionId);
            if (session == null)
            {
                _logger.LogWarning("No session found for connection {ConnectionId}", context.ConnectionId);
                session = _sessionManager.CreateSession(context.ConnectionId);
            }

            if (!_crypto.VerifyMac(messageData.Span, session.MacKey.Span, receivedMac.Span))
            {
                _logger.LogWarning("Invalid MAC for message {SequenceId} from connection {ConnectionId}",
                    message.SequenceId, context.ConnectionId);
                return;
            }

            _sessionManager.UpdateActivity(context.ConnectionId);

            // Route message
            await _router.RouteAsync(context, message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message from connection {ConnectionId}",
                context.ConnectionId);
        }
    }
}
