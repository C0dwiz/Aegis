using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;
using Aegis.Common;
using Aegis.Common.Logging;
using Aegis.Common.Errors;

namespace Aegis.Transport;

public class TcpServer : IDisposable
{
    private readonly ILogger _logger;
    private readonly Socket _listener;
    private readonly CancellationTokenSource _cts;
    private readonly ConcurrentDictionary<ulong, ConnectionContext> _connections;
    private readonly RateLimiter? _rateLimiter;
    private ulong _nextConnectionId;
    private bool _disposed;
    
    public int Port { get; }
    public int MaxConnections { get; }
    public int BufferSize { get; }
    public bool EnableIPv6 { get; }
    public TimeSpan IdleTimeout { get; }
    
    public event Func<ConnectionContext, ReadOnlyMemory<byte>, Task>? OnMessageReceived;
    public event Action<ConnectionContext>? OnClientConnected;
    public event Action<ConnectionContext>? OnClientDisconnected;
    
    public TcpServer(
        int port,
        int maxConnections = 10000,
        int bufferSize = 8192,
        bool enableIPv6 = false,
        int idleTimeoutSeconds = 300,
        RateLimiter? rateLimiter = null,
        ILogger? logger = null)
    {
        Port = port;
        MaxConnections = maxConnections;
        BufferSize = bufferSize;
        EnableIPv6 = enableIPv6;
        IdleTimeout = TimeSpan.FromSeconds(Math.Max(5, idleTimeoutSeconds));
        _rateLimiter = rateLimiter;
        _logger = logger ?? new NullLogger();
        _listener = CreateListener(enableIPv6);
        _cts = new CancellationTokenSource();
        _connections = new ConcurrentDictionary<ulong, ConnectionContext>();
    }
    
    public async Task StartAsync(int port = 0)
    {
        if (port == 0) port = Port;
        var endpoint = EnableIPv6
            ? new IPEndPoint(IPAddress.IPv6Any, port)
            : new IPEndPoint(IPAddress.Any, port);
        _listener.Bind(endpoint);
        _listener.Listen(MaxConnections);
        
        _logger.Info($"TCP server started on port {port}");
        
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                var socket = await _listener.AcceptAsync();
                if (!CanAcceptConnection(socket))
                {
                    socket.Dispose();
                    continue;
                }

                _ = HandleConnectionAsync(socket);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException) when (_cts.Token.IsCancellationRequested)
            {
                break;
            }
            catch (SocketException) when (_cts.Token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Error("Error accepting connection", ex);

                if (_cts.Token.IsCancellationRequested)
                    break;
            }
        }
    }
    
    private async Task HandleConnectionAsync(Socket socket)
    {
        var connectionId = Interlocked.Increment(ref _nextConnectionId);
        var context = new ConnectionContext(socket, connectionId, BufferSize);
        var remoteIp = GetRemoteIp(socket);
        
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
            if (remoteIp != null)
            {
                _rateLimiter?.RecordDisconnection(remoteIp);
            }
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
            int bytesReceived;
            using (var idleTimeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token))
            {
                idleTimeout.CancelAfter(IdleTimeout);
                try
                {
                    bytesReceived = await socket.ReceiveAsync(buffer, SocketFlags.None, idleTimeout.Token);
                }
                catch (OperationCanceledException) when (!_cts.Token.IsCancellationRequested)
                {
                    _logger.Warning($"Connection {context.ConnectionId} closed due to idle timeout ({IdleTimeout.TotalSeconds:F0}s)");
                    break;
                }
            }

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
    
    public async Task SendToConnectionAsync(ulong connectionId, ReadOnlyMemory<byte> data)
    {
        if (!_connections.TryGetValue(connectionId, out var context))
            throw new TransportError($"Connection {connectionId} not found");
        
        await SendAsync(context, data);
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

    private bool CanAcceptConnection(Socket socket)
    {
        var remoteIp = GetRemoteIp(socket);
        if (remoteIp == null)
        {
            return true;
        }

        return _rateLimiter?.CanConnect(remoteIp) ?? true;
    }

    private static string? GetRemoteIp(Socket socket)
    {
        return (socket.RemoteEndPoint as IPEndPoint)?.Address.ToString();
    }

    private static Socket CreateListener(bool enableIPv6)
    {
        if (!enableIPv6)
        {
            return new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        }

        var listener = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp)
        {
            DualMode = true
        };
        return listener;
    }
}

public class NullLogger : ILogger
{
    public void Debug(string message) { }
    public void Info(string message) { }
    public void Warning(string message) { }
    public void Error(string message, Exception? ex = null) { }
}
