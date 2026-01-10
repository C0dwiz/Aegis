using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;
using Aegis.Common.Logging;
using Aegis.Common.Errors;

namespace Aegis.Transport;

public class TcpServer : IDisposable
{
    private readonly ILogger _logger;
    private readonly Socket _listener;
    private readonly CancellationTokenSource _cts;
    private readonly ConcurrentDictionary<ulong, ConnectionContext> _connections;
    private ulong _nextConnectionId;
    private bool _disposed;
    
    public int Port { get; }
    public int MaxConnections { get; }
    public int BufferSize { get; }
    
    public event Func<ConnectionContext, ReadOnlyMemory<byte>, Task>? OnMessageReceived;
    public event Action<ConnectionContext>? OnClientConnected;
    public event Action<ConnectionContext>? OnClientDisconnected;
    
    public TcpServer(int port, int maxConnections = 10000, int bufferSize = 8192, ILogger? logger = null)
    {
        Port = port;
        MaxConnections = maxConnections;
        BufferSize = bufferSize;
        _logger = logger ?? new NullLogger();
        _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _cts = new CancellationTokenSource();
        _connections = new ConcurrentDictionary<ulong, ConnectionContext>();
    }
    
    public async Task StartAsync(int port = 0)
    {
        if (port == 0) port = Port;
        var endpoint = new IPEndPoint(IPAddress.Any, port);
        _listener.Bind(endpoint);
        _listener.Listen(MaxConnections);
        
        _logger.Info($"TCP server started on port {port}");
        
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                var socket = await _listener.AcceptAsync(_cts.Token);
                _ = HandleConnectionAsync(socket);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Error("Error accepting connection", ex);
            }
        }
    }
    
    private async Task HandleConnectionAsync(Socket socket)
    {
        var connectionId = Interlocked.Increment(ref _nextConnectionId);
        var context = new ConnectionContext(socket, connectionId, BufferSize);
        
        if (!_connections.TryAdd(connectionId, context))
        {
            socket.Dispose();
            return;
        }
        
        OnClientConnected?.Invoke(context);
        _logger.Info($"Client {connectionId} connected from {socket.RemoteEndPoint}");
        
        try
        {
            await ProcessConnectionAsync(context);
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            _logger.Warning($"Connection {connectionId} closed: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Error processing connection {connectionId}", ex);
        }
        finally
        {
            _connections.TryRemove(connectionId, out _);
            OnClientDisconnected?.Invoke(context);
            context.Dispose();
        }
    }
    
    private async Task ProcessConnectionAsync(ConnectionContext context)
    {
        var socket = context.Socket;
        var buffer = context.GetReceiveBuffer();
        
        while (!_cts.Token.IsCancellationRequested && socket.Connected)
        {
            var bytesReceived = await socket.ReceiveAsync(buffer, SocketFlags.None, _cts.Token);
            if (bytesReceived == 0)
            {
                // Клиент отключился порядком
                break;
            }
            
            context.UpdateActivity();
            
            if (OnMessageReceived != null)
            {
                await OnMessageReceived(context, buffer.Slice(0, bytesReceived));
            }
        }
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        
        var stopTask = StopAsync();
        _listener.Dispose();
        _cts.Dispose();
        
        _disposed = true;
        GC.SuppressFinalize(this);
    }
    
    public async Task SendAsync(ConnectionContext context, ReadOnlyMemory<byte> data)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        
        var socket = context.Socket;
        if (!socket.Connected)
            throw new TransportError("Socket not connected");
        
        try
        {
            await socket.SendAsync(data, SocketFlags.None, _cts.Token);
            context.UpdateActivity();
        }
        catch (SocketException ex)
        {
            throw new TransportError($"Socket error: {ex.SocketErrorCode}", ex);
        }
    }
    
    public async Task StopAsync()
    {
        _cts.Cancel();
        _listener.Close();
        
        foreach (var connection in _connections.Values)
        {
            try { connection.Socket.Shutdown(SocketShutdown.Both); }
            catch { }
            connection.Dispose();
        }
        
        _connections.Clear();
        _logger.Info("TCP server stopped");
    }
}

public class NullLogger : ILogger
{
    public void Debug(string message) { }
    public void Info(string message) { }
    public void Warning(string message) { }
    public void Error(string message, Exception? ex = null) { }
}
