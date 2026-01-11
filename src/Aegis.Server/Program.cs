using Aegis.Common.Logging;
using Aegis.Transport;
using Aegis.Handlers;
using Aegis.Crypto;
using Aegis.Protocol;
using Aegis.Common;

namespace Aegis.Server;

public class ConsoleLogger : ILogger
{
    public void Debug(string message) => 
        Console.WriteLine($"[DEBUG] {DateTime.Now:HH:mm:ss} {message}");
    
    public void Info(string message) => 
        Console.WriteLine($"[INFO] {DateTime.Now:HH:mm:ss} {message}");
    
    public void Warning(string message) => 
        Console.WriteLine($"[WARN] {DateTime.Now:HH:mm:ss} {message}");
    
    public void Error(string message, Exception? ex = null)
    {
        Console.WriteLine($"[ERROR] {DateTime.Now:HH:mm:ss} {message}");
        if (ex != null) Console.WriteLine(ex);
    }
}

public static class Program
{
    private static TcpServer? _server;
    private static CancellationTokenSource _cts = new();
    private static SessionManager? _sessionManager;
    
    public static async Task Main(string[] args)
    {
        Console.CancelKeyPress += OnCancelKeyPress;
        
        var logger = new ConsoleLogger();
        logger.Info("Starting Aegis Messenger Server...");
        
        try
        {
            // Configuration
            int port = GetPortFromConfig(args);
            int maxConnections = 10000;
            
            // Initialize components
            var cryptoProvider = new AegisCryptoProvider();
            var antiSpam = new AntiSpamClient();
            _sessionManager = new SessionManager((ISessionCryptoProvider)cryptoProvider, logger);
            
            // Create message sender for handlers
            var messageSender = new ServerMessageSender(_server, cryptoProvider, _sessionManager, logger);
            
            // Create handlers
            var handlers = new IMessageHandler[]
            {
                new AuthHandler(antiSpam, messageSender, cryptoProvider, logger),
                new PingHandler(),
                new MessageHandler(antiSpam, messageSender, cryptoProvider, logger)
            };
            
            var router = new MessageRouter(handlers, cryptoProvider, _sessionManager, logger);
            
            // Create and start server
            _server = new TcpServer(port, maxConnections, logger: logger);
            _server.OnClientConnected += OnClientConnected;
            _server.OnClientDisconnected += OnClientDisconnected;
            _server.OnMessageReceived += async (context, data) =>
            {
                await ProcessMessageAsync(context, data, router, cryptoProvider, logger);
            };
            
            logger.Info($"Server configured on port {port}, max connections: {maxConnections}");
            logger.Info("Press Ctrl+C to stop the server");
            
            await _server.StartAsync();
        }
        catch (Exception ex)
        {
            logger.Error("Fatal error starting server", ex);
            Environment.Exit(1);
        }
    }
    
    private static int GetPortFromConfig(string[] args)
    {
        // Порт по умолчанию, может быть переопределен аргументами или файлом конфигурации
        if (args.Length > 0 && int.TryParse(args[0], out int port))
            return port;
        
        return 8888; // Default port
    }
    
    private static async Task ProcessMessageAsync(
        ConnectionContext context, 
        ReadOnlyMemory<byte> data,
        MessageRouter router,
        ICryptoProvider crypto,
        ILogger logger)
    {
        try
        {
            // Дешифруем и проверяем MAC перед обработкой
            var message = MessageEncoder.Decode(data.Span);
            
            // Проверяем MAC с использованием ключей сессии
            var messageData = data.Slice(0, data.Length - ProtocolConstants.MacSize);
            var receivedMac = data.Slice(data.Length - ProtocolConstants.MacSize, ProtocolConstants.MacSize);
            
            // Реализовать правильное управление ключами сессии
            var session = _sessionManager?.GetSession(context.ConnectionId);
            if (session == null)
            {
                logger.Warning($"No session found for connection {context.ConnectionId}, creating new session");
                session = _sessionManager?.CreateSession(context.ConnectionId);
            }
            
            if (session != null && !crypto.VerifyMac(messageData.Span, session.MacKey.Span, receivedMac.Span))
            {
                logger.Warning($"Invalid MAC for message {message.SequenceId} from connection {context.ConnectionId}");
                await SendErrorToClient(context, message.SequenceId, "Invalid MAC", crypto, session);
                return;
            }
            
            // Update session activity
            _sessionManager?.UpdateActivity(context.ConnectionId);
            
            await router.RouteAsync(context, message);
        }
        catch (Exception ex)
        {
            logger.Error($"Error processing message from connection {context.ConnectionId}", ex);
            await SendErrorToClient(context, 0, "Message processing error", crypto, _sessionManager?.GetSession(context.ConnectionId));
        }
    }
    
    private static void OnClientConnected(ConnectionContext context)
    {
        // Соединение установлено, инициализация сессии
        _sessionManager?.CreateSession(context.ConnectionId);
        Console.WriteLine($"Client {context.ConnectionId} connected");
    }
    
    private static void OnClientDisconnected(ConnectionContext context)
    {
        // Удаление сессии при отключении
        _sessionManager?.RemoveSession(context.ConnectionId);
        Console.WriteLine($"Client {context.ConnectionId} disconnected");
    }
    
    private static async Task SendErrorToClient(
        ConnectionContext context, 
        ulong sequenceId, 
        string error,
        ICryptoProvider crypto,
        SessionInfo? session)
    {
        try
        {
            var errorMessage = new Message
            {
                Magic = ProtocolConstants.Magic,
                VersionMajor = ProtocolConstants.VersionMajor,
                VersionMinor = ProtocolConstants.VersionMinor,
                Type = MessageType.Error,
                SequenceId = sequenceId,
                PayloadLength = (uint)System.Text.Encoding.UTF8.GetByteCount(error),
                Payload = System.Text.Encoding.UTF8.GetBytes(error)
            };
            
            // Encrypt and sign the error message
            if (session != null)
            {
                var encryptedMessage = await crypto.EncryptMessageAsync(errorMessage, session.SessionKey.ToArray());
                await _server?.SendAsync(context, encryptedMessage)!;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending error to client {context.ConnectionId}: {ex.Message}");
        }
    }
    
    private static void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        Console.WriteLine("\nShutting down server...");
        _cts.Cancel();
        _server?.StopAsync().Wait();
    }
}
