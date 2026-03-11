using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;
using System.Buffers;
using System.Threading.Channels;
using System.Text;
using Aegis.Common;
using Aegis.Common.Logging;
using Aegis.Common.Errors;

namespace Aegis.Transport;

public class TcpServer : IDisposable
{
    private sealed class PendingFrame
    {
        public required byte[] Buffer { get; init; }
        public required int Length { get; init; }
    }

    private readonly ILogger _logger;
    private readonly Socket _listener;
    private readonly CancellationTokenSource _cts;
    private readonly ConcurrentDictionary<ulong, ConnectionContext> _connections;
    private readonly RateLimiter? _rateLimiter;
    private readonly TimeSpan _partialFrameTimeout;
    private readonly int _maxIncompleteFrameDrops;
    private readonly byte[] _transportMaskingKey;
    private ulong _nextConnectionId;
    private bool _disposed;
    
    public int Port { get; }
    public int MaxConnections { get; }
    public int BufferSize { get; }
    public bool EnableIPv6 { get; }
    public TimeSpan IdleTimeout { get; }
    public bool EnableTransportMasking => _transportMaskingKey.Length > 0;
    
    public event Func<ConnectionContext, ReadOnlyMemory<byte>, Task>? OnMessageReceived;
    public event Action<ConnectionContext>? OnClientConnected;
    public event Action<ConnectionContext>? OnClientDisconnected;
    
    public TcpServer(
        int port,
        int maxConnections = 10000,
        int bufferSize = 8192,
        bool enableIPv6 = false,
        int idleTimeoutSeconds = 300,
        int partialFrameTimeoutMs = 300,
        int maxIncompleteFrameDrops = 3,
        string? transportMaskingKey = null,
        RateLimiter? rateLimiter = null,
        ILogger? logger = null)
    {
        Port = port;
        MaxConnections = maxConnections;
        BufferSize = bufferSize;
        EnableIPv6 = enableIPv6;
        IdleTimeout = TimeSpan.FromSeconds(Math.Max(5, idleTimeoutSeconds));
        _partialFrameTimeout = TimeSpan.FromMilliseconds(Math.Clamp(partialFrameTimeoutMs, 50, 5000));
        _maxIncompleteFrameDrops = Math.Max(1, maxIncompleteFrameDrops);
        _transportMaskingKey = string.IsNullOrWhiteSpace(transportMaskingKey)
            ? Array.Empty<byte>()
            : Encoding.UTF8.GetBytes(transportMaskingKey);
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
        var frameQueue = Channel.CreateBounded<PendingFrame>(new BoundedChannelOptions(256)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });

        var consumerTask = ConsumeFramesAsync(context, frameQueue.Reader);
        
        try
        {
            while (!_cts.Token.IsCancellationRequested && socket.Connected)
            {
                int bytesReceived;
                using (var idleTimeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token))
                {
                    var waitMs = GetReceiveWaitMs(context);
                    idleTimeout.CancelAfter(TimeSpan.FromMilliseconds(waitMs));
                    try
                    {
                        bytesReceived = await socket.ReceiveAsync(buffer, SocketFlags.None, idleTimeout.Token);
                    }
                    catch (OperationCanceledException) when (!_cts.Token.IsCancellationRequested)
                    {
                        if (context.TryDropExpiredIncompleteFrame(_partialFrameTimeout, out var droppedBytes))
                        {
                            _logger.Warning(
                                $"Connection {context.ConnectionId}: dropped incomplete frame buffer ({droppedBytes} bytes) after timeout {_partialFrameTimeout.TotalMilliseconds:F0}ms [{context.IncompleteFrameDropCount}/{_maxIncompleteFrameDrops}]");

                            if (context.IncompleteFrameDropCount >= _maxIncompleteFrameDrops)
                            {
                                _logger.Warning(
                                    $"Connection {context.ConnectionId} closed after exceeding incomplete-frame drop threshold ({_maxIncompleteFrameDrops})");
                                break;
                            }

                            continue;
                        }

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

                var incomingChunk = buffer.Span.Slice(0, bytesReceived);
                if (EnableTransportMasking)
                {
                    context.ApplyInboundMaskInPlace(incomingChunk, _transportMaskingKey);
                }

                context.AppendIncomingData(incomingChunk);

                while (context.TryReadNextFrame(out var frame, out var frameLength))
                {
                    await frameQueue.Writer.WriteAsync(new PendingFrame
                    {
                        Buffer = frame,
                        Length = frameLength
                    }, _cts.Token);
                }
            }
        }
        finally
        {
            frameQueue.Writer.TryComplete();
            await consumerTask;
        }
    }

    private int GetReceiveWaitMs(ConnectionContext context)
    {
        var idleMs = Math.Max(1, (int)Math.Ceiling(IdleTimeout.TotalMilliseconds));
        if (!context.HasPendingIncomingData)
        {
            return idleMs;
        }

        var incompleteMs = context.GetRemainingIncompleteFrameWaitMs(_partialFrameTimeout);
        return Math.Max(1, Math.Min(idleMs, incompleteMs));
    }

    private async Task ConsumeFramesAsync(ConnectionContext context, ChannelReader<PendingFrame> reader)
    {
        await foreach (var frame in reader.ReadAllAsync(_cts.Token))
        {
            try
            {
                if (OnMessageReceived != null)
                {
                    await OnMessageReceived(context, frame.Buffer.AsMemory(0, frame.Length));
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(frame.Buffer);
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
            if (!EnableTransportMasking)
            {
                await socket.SendAsync(data, SocketFlags.None, _cts.Token);
            }
            else
            {
                var rented = ArrayPool<byte>.Shared.Rent(data.Length);
                try
                {
                    var span = rented.AsSpan(0, data.Length);
                    data.Span.CopyTo(span);
                    context.ApplyOutboundMaskInPlace(span, _transportMaskingKey);
                    await socket.SendAsync(rented.AsMemory(0, data.Length), SocketFlags.None, _cts.Token);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rented);
                }
            }

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
