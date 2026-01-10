using Xunit;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Aegis.Transport;
using Aegis.Common.Logging;

namespace Aegis.Tests;

public class TransportTests
{
    private readonly TestLogger _logger = new TestLogger();
    
    [Fact]
    public async Task TcpServer_StartStop_ShouldWorkCorrectly()
    {
        // Arrange
        var server = new TcpServer(0, 10000, 8192, _logger);
        var startedTcs = new TaskCompletionSource<bool>();
        
        server.OnClientConnected += (ctx) => startedTcs.SetResult(true);
        
        // Act
        await server.StartAsync();
        
        // Assert
        Assert.True(await startedTcs.Task);
        
        // Cleanup
        await server.StopAsync();
    }
    
    [Fact]
    public async Task TcpServer_ClientConnection_ShouldTriggerEvents()
    {
        // Arrange
        var server = new TcpServer(0, 10000, 8192, _logger);
        var connectedTcs = new TaskCompletionSource<ConnectionContext>();
        var disconnectedTcs = new TaskCompletionSource<ConnectionContext>();
        
        server.OnClientConnected += connectedTcs.SetResult;
        server.OnClientDisconnected += disconnectedTcs.SetResult;
        
        await server.StartAsync();
        
        // Act
        using var client = new TcpClient();
        await client.ConnectAsync(System.Net.IPAddress.Loopback, server.Port);
        
        // Assert
        var connectedContext = await connectedTcs.Task;
        Assert.NotNull(connectedContext);
        Assert.NotEqual(0ul, connectedContext.ConnectionId);
        
        // Cleanup
        client.Close();
        var disconnectedContext = await disconnectedTcs.Task;
        Assert.Equal(connectedContext.ConnectionId, disconnectedContext.ConnectionId);
        
        await server.StopAsync();
    }
    
    [Fact]
    public void ConnectionContext_UpdateActivity_ShouldWork()
    {
        // Arrange
        var connectionId = 12345ul;
        var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
        var context = new ConnectionContext(socket, connectionId);
        var initialActivity = context.LastActivity;
        
        // Act
        System.Threading.Thread.Sleep(10);
        context.UpdateActivity();
        var updatedActivity = context.LastActivity;
        
        // Assert
        Assert.True(updatedActivity > initialActivity);
        socket.Dispose();
    }
    
    [Fact]
    public void ConnectionContext_GetNextSequenceId_ShouldIncrement()
    {
        // Arrange
        var connectionId = 12345ul;
        var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
        var context = new ConnectionContext(socket, connectionId);
        
        // Act & Assert
        var seq1 = context.GetNextSequenceId();
        var seq2 = context.GetNextSequenceId();
        var seq3 = context.GetNextSequenceId();
        
        Assert.Equal(1ul, seq1);
        Assert.Equal(2ul, seq2);
        Assert.Equal(3ul, seq3);
        
        socket.Dispose();
    }
    
    [Fact]
    public void ConnectionContext_GetReceiveBuffer_ShouldReturnValidBuffer()
    {
        // Arrange
        var connectionId = 12345ul;
        var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
        var context = new ConnectionContext(socket, connectionId);
        
        // Act
        var buffer1 = context.GetReceiveBuffer();
        var buffer2 = context.GetReceiveBuffer();
        
        // Assert
        Assert.Equal(8192, buffer1.Length);
        Assert.Equal(8192, buffer2.Length);
        Assert.Equal(buffer1, buffer2); // Should return same buffer instance
        
        socket.Dispose();
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
}
