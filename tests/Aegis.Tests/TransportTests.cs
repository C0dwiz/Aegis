using Xunit;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Aegis.Transport;
using Aegis.Common.Logging;
using Aegis.Protocol;

namespace Aegis.Tests;

public class TransportTests
{
    private readonly TestLogger _logger = new TestLogger();
    private const int TestTimeoutMs = 3000;
    
    [Fact]
    public async Task TcpServer_StartStop_ShouldWorkCorrectly()
    {
        using var cts = new CancellationTokenSource(TestTimeoutMs);
        
        // Arrange
        var server = new TcpServer(0, 100, 1024, false, 300, null, _logger);
        
        try
        {
            // Act
            var startTask = Task.Run(() => server.StartAsync(0), cts.Token);
            await Task.Delay(200); // Give server time to start and bind
            
            // Assert - server should be running
            Assert.NotNull(server);
            // Note: Port property might not be updated when using port 0, but server should start
            // We verify it's running by checking that the task is still running
            Assert.False(startTask.IsCompleted);
        }
        finally
        {
            // Cleanup
            await server.StopAsync();
            cts.Cancel();
        }
    }
    
    [Fact]
    public async Task TcpServer_ClientConnection_ShouldTriggerEvents()
    {
        using var cts = new CancellationTokenSource(TestTimeoutMs);
        var port = GetFreeTcpPort();
        
        // Arrange
        var server = new TcpServer(port, 100, 1024, false, 300, null, _logger);
        var connectedTcs = new TaskCompletionSource<ConnectionContext>();
        var disconnectedTcs = new TaskCompletionSource<ConnectionContext>();
        
        server.OnClientConnected += ctx => connectedTcs.TrySetResult(ctx);
        server.OnClientDisconnected += ctx => disconnectedTcs.TrySetResult(ctx);
        
        var startTask = Task.Run(() => server.StartAsync(port), cts.Token);
        await Task.Delay(200); // Give server time to start
        
        try
        {
            // Act
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port, cts.Token);
            
            var connectedContext = await connectedTcs.Task
                .WaitAsync(TimeSpan.FromMilliseconds(500), cts.Token);
            
            // Assert
            Assert.NotNull(connectedContext);
            Assert.NotEqual(0ul, connectedContext.ConnectionId);
            
            client.Close();
            
            var disconnectedContext = await disconnectedTcs.Task
                .WaitAsync(TimeSpan.FromMilliseconds(500), cts.Token);
            
            Assert.Equal(connectedContext.ConnectionId, disconnectedContext.ConnectionId);
        }
        catch (TimeoutException)
        {
            await server.StopAsync();
            throw;
        }
        finally
        {
            await server.StopAsync();
            cts.Cancel();
        }
    }
    
    [Fact]
    public void ConnectionContext_UpdateActivity_ShouldWork()
    {
        // Arrange
        var connectionId = 12345ul;
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var context = new ConnectionContext(socket, connectionId);
        var initialActivity = context.LastActivity;
        
        // Act
        context.UpdateActivity();
        var updatedActivity = context.LastActivity;
        
        // Assert
        Assert.True(updatedActivity >= initialActivity, 
            $"Updated activity ({updatedActivity}) should be greater than or equal to initial ({initialActivity})");
    }
    
    [Fact]
    public void ConnectionContext_GetNextSequenceId_ShouldIncrement()
    {
        // Arrange
        var connectionId = 12345ul;
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var context = new ConnectionContext(socket, connectionId);
        
        // Act & Assert
        var seq1 = context.GetNextSequenceId();
        var seq2 = context.GetNextSequenceId();
        var seq3 = context.GetNextSequenceId();
        
        Assert.Equal(1ul, seq1);
        Assert.Equal(2ul, seq2);
        Assert.Equal(3ul, seq3);
    }
    
    [Fact]
    public void ConnectionContext_GetReceiveBuffer_ShouldReturnValidBuffer()
    {
        // Arrange
        var connectionId = 12345ul;
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var context = new ConnectionContext(socket, connectionId, 512); // Smaller buffer for test
        
        // Act
        var buffer1 = context.GetReceiveBuffer();
        var buffer2 = context.GetReceiveBuffer();
        
        // Assert
        Assert.Equal(512, buffer1.Length);
        Assert.Equal(512, buffer2.Length);
        Assert.Equal(buffer1, buffer2); // Should return same buffer instance
    }
    
    [Fact]
    public void ConnectionContext_GetSendBuffer_ShouldReturnValidBuffer()
    {
        // Arrange
        var connectionId = 12345ul;
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var context = new ConnectionContext(socket, connectionId, 512);
        
        // Act
        var buffer1 = context.GetSendBuffer();
        var buffer2 = context.GetSendBuffer();
        
        // Assert
        Assert.Equal(512, buffer1.Length);
        Assert.Equal(512, buffer2.Length);
        Assert.Equal(buffer1, buffer2); // Should return same buffer instance
    }

    [Fact]
    public void ConnectionContext_TryReadNextFrame_ShouldHandlePartialAndCoalescedFrames()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var context = new ConnectionContext(socket, 777ul, 256);

        var msg1 = new Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = ProtocolConstants.VersionMajor,
            VersionMinor = ProtocolConstants.VersionMinor,
            Type = MessageType.Ping,
            SequenceId = 1,
            Payload = new byte[] { 1, 2, 3 },
            PayloadLength = 3,
            Mac = new byte[ProtocolConstants.MacSize]
        };

        var msg2 = new Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = ProtocolConstants.VersionMajor,
            VersionMinor = ProtocolConstants.VersionMinor,
            Type = MessageType.Ping,
            SequenceId = 2,
            Payload = new byte[] { 4, 5, 6, 7 },
            PayloadLength = 4,
            Mac = new byte[ProtocolConstants.MacSize]
        };

        var frame1 = new byte[Message.TotalSize(msg1)];
        var frame2 = new byte[Message.TotalSize(msg2)];
        MessageEncoder.Encode(msg1, frame1);
        MessageEncoder.Encode(msg2, frame2);

        var combined = new byte[frame1.Length + frame2.Length];
        frame1.CopyTo(combined, 0);
        frame2.CopyTo(combined, frame1.Length);

        var firstChunkSize = 10;
        context.AppendIncomingData(combined.AsSpan(0, firstChunkSize));
        Assert.False(context.TryReadNextFrame(out _));

        context.AppendIncomingData(combined.AsSpan(firstChunkSize));

        Assert.True(context.TryReadNextFrame(out var parsedFrame1));
        Assert.True(context.TryReadNextFrame(out var parsedFrame2));
        Assert.False(context.TryReadNextFrame(out _));

        Assert.Equal(frame1, parsedFrame1);
        Assert.Equal(frame2, parsedFrame2);
    }
    
    [Fact]
    public void ConnectionContext_Constructor_ShouldInitializeCorrectly()
    {
        // Arrange
        var connectionId = 12345ul;
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        
        // Act
        var context = new ConnectionContext(socket, connectionId, 1024);
        
        // Assert
        Assert.Equal(socket, context.Socket);
        Assert.Equal(connectionId, context.ConnectionId);
        Assert.Equal(0ul, context.NextSequenceId);
        Assert.True(context.LastActivity > DateTime.MinValue);
    }
    
    [Fact]
    public void TcpServer_Constructor_ShouldInitializeCorrectly()
    {
        // Act
        var server = new TcpServer(8080, 100, 2048, false, 300, null, _logger);
        
        // Assert
        Assert.Equal(8080, server.Port);
        Assert.Equal(100, server.MaxConnections);
        Assert.Equal(2048, server.BufferSize);
    }
    
    [Fact]
    public void TcpServer_SendAsync_WithNullContext_ShouldThrowException()
    {
        // Arrange
        var server = new TcpServer(8080, 100, 2048, false, 300, null, _logger);
        
        // Act & Assert
        Assert.ThrowsAsync<ArgumentNullException>(async () => 
            await server.SendAsync(null!, new byte[] { 1, 2, 3 }));
    }
    
    private class TestLogger : ILogger
    {
        public List<string> DebugMessages { get; } = new();
        public List<string> InfoMessages { get; } = new();
        public List<string> WarningMessages { get; } = new();
        public List<string> ErrorMessages { get; } = new();
        
        public void Debug(string message) => DebugMessages.Add(message);
        public void Info(string message) => InfoMessages.Add(message);
        public void Warning(string message) => WarningMessages.Add(message);
        public void Error(string message, Exception? ex = null) 
        {
            ErrorMessages.Add(message);
            if (ex != null) ErrorMessages.Add(ex.ToString());
        }
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var port = endpoint.Port;
        listener.Stop();
        return port;
    }
}