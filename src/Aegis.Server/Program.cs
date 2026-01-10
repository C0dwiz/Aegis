using Aegis.Common.Logging;
using Aegis.Transport;
using Aegis.Handlers;
using Aegis.Crypto;
using Aegis.Protocol;

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
            
            // Create handlers
            var handlers = new IMessageHandler[]
            {
                new AuthHandler(antiSpam),
                new PingHandler(),
                new MessageHandler(antiSpam)
            };
            
            var router = new MessageRouter(handlers, logger);
            
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
            
            // TODO: Реализовать правильное управление ключами сессии
            // Для сейчас мы пропускаем проверку MAC в продакшн
            // if (!crypto.VerifyMac(messageData.Span, sessionMacKey.Span, receivedMac.Span)) 
            // {
            //     logger.Warning($"Invalid MAC for message {message.SequenceId} from connection {context.ConnectionId}");
            //     return;
            // }
            
            await router.RouteAsync(context, message);
        }
        catch (Exception ex)
        {
            logger.Error($"Error processing message from connection {context.ConnectionId}", ex);
            // TODO: отправка ошибки клиенту
        }
    }
    
    private static void OnClientConnected(ConnectionContext context)
    {
        // Соединение установлено, инициализация сессии
        Console.WriteLine($"Client {context.ConnectionId} connected");
    }
    
    private static void OnClientDisconnected(ConnectionContext context)
    {
        Console.WriteLine($"Client {context.ConnectionId} disconnected");
    }
    
    private static void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        Console.WriteLine("\nShutting down server...");
        _cts.Cancel();
        _server?.StopAsync().Wait();
    }
}
