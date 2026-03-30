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
    private const int TestTimeoutMs = 8000;

    [Fact]
    public async Task TcpServer_StartStop_ShouldWorkCorrectly()
    {
        using var cts = new CancellationTokenSource(TestTimeoutMs);

        // Arrange
        var server = new TcpServer(0, 100, 1024, false, 300, rateLimiter: null, logger: _logger);

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
        var server = new TcpServer(port, 100, 1024, false, 300, rateLimiter: null, logger: _logger);
        var connectedTcs = new TaskCompletionSource<ConnectionContext>();
        var disconnectedTcs = new TaskCompletionSource<ConnectionContext>();

        server.OnClientConnected += ctx => connectedTcs.TrySetResult(ctx);
        server.OnClientDisconnected += ctx => disconnectedTcs.TrySetResult(ctx);

        var startTask = Task.Run(() => server.StartAsync(port), cts.Token);
        await Task.Delay(100, cts.Token);

        try
        {
            // Act
            using var client = new TcpClient();
            await ConnectWithRetryAsync(client, IPAddress.Loopback, port, cts.Token);

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
    public async Task TcpServer_WithTransportMasking_ShouldDecodeInboundMaskedFrames()
    {
        using var cts = new CancellationTokenSource(TestTimeoutMs);
        var port = GetFreeTcpPort();
        const string maskingKey = "test-mask-key";

        var server = new TcpServer(
            port,
            100,
            1024,
            false,
            300,
            transportMaskingKey: maskingKey,
            rateLimiter: null,
            logger: _logger);

        var messageTcs = new TaskCompletionSource<byte[]>();
        server.OnMessageReceived += (ctx, data) =>
        {
            messageTcs.TrySetResult(data.ToArray());
            return Task.CompletedTask;
        };

        var startTask = Task.Run(() => server.StartAsync(port), cts.Token);
        await Task.Delay(100, cts.Token);

        try
        {
            using var client = new TcpClient();
            await ConnectWithRetryAsync(client, IPAddress.Loopback, port, cts.Token);

            var originalMessage = new Message
            {
                Magic = ProtocolConstants.Magic,
                VersionMajor = ProtocolConstants.VersionMajor,
                VersionMinor = ProtocolConstants.VersionMinor,
                Type = MessageType.Ping,
                SequenceId = 42,
                Payload = new byte[] { 1, 2, 3, 4 },
                PayloadLength = 4,
            };

            var originalFrame = new byte[Message.TotalSize(originalMessage)];
            MessageEncoder.Encode(originalMessage, originalFrame);

            var maskedFrame = ApplyMask(originalFrame, System.Text.Encoding.UTF8.GetBytes(maskingKey), 0);

            await client.GetStream().WriteAsync(maskedFrame, cts.Token);
            await client.GetStream().FlushAsync(cts.Token);

            var receivedFrame = await messageTcs.Task.WaitAsync(TimeSpan.FromMilliseconds(1500), cts.Token);
            Assert.Equal(originalFrame, receivedFrame);
        }
        finally
        {
            await server.StopAsync();
            cts.Cancel();
        }
    }

    [Fact]
    public async Task TcpServer_WithTransportMasking_ShouldMaskOutboundFrames()
    {
        using var cts = new CancellationTokenSource(TestTimeoutMs);
        var port = GetFreeTcpPort();
        const string maskingKey = "test-mask-key";

        var server = new TcpServer(
            port,
            100,
            1024,
            false,
            300,
            transportMaskingKey: maskingKey,
            rateLimiter: null,
            logger: _logger);

        var connectedTcs = new TaskCompletionSource<ConnectionContext>();
        server.OnClientConnected += ctx => connectedTcs.TrySetResult(ctx);

        _ = Task.Run(() => server.StartAsync(port), cts.Token);
        await Task.Delay(100, cts.Token);

        try
        {
            using var client = new TcpClient();
            await ConnectWithRetryAsync(client, IPAddress.Loopback, port, cts.Token);

            var connectedContext = await connectedTcs.Task
                .WaitAsync(TimeSpan.FromMilliseconds(500), cts.Token);

            var originalMessage = new Message
            {
                Magic = ProtocolConstants.Magic,
                VersionMajor = ProtocolConstants.VersionMajor,
                VersionMinor = ProtocolConstants.VersionMinor,
                Type = MessageType.Ping,
                SequenceId = 99,
                Payload = new byte[] { 9, 8, 7, 6, 5 },
                PayloadLength = 5,
            };

            var originalFrame = new byte[Message.TotalSize(originalMessage)];
            MessageEncoder.Encode(originalMessage, originalFrame);

            await server.SendToConnectionAsync(connectedContext.ConnectionId, originalFrame);

            var stream = client.GetStream();
            var received = new byte[originalFrame.Length];
            var read = 0;
            while (read < received.Length)
            {
                var n = await stream.ReadAsync(received.AsMemory(read, received.Length - read), cts.Token);
                if (n == 0)
                {
                    break;
                }
                read += n;
            }

            Assert.Equal(originalFrame.Length, read);

            var expectedMasked = ApplyMask(originalFrame, System.Text.Encoding.UTF8.GetBytes(maskingKey), 0);
            Assert.Equal(expectedMasked, received);

            var unmasked = ApplyMask(received, System.Text.Encoding.UTF8.GetBytes(maskingKey), 0);
            Assert.Equal(originalFrame, unmasked);
        }
        finally
        {
            await server.StopAsync();
            cts.Cancel();
        }
    }

    [Fact]
    public async Task TcpServer_ShouldProcessBurstOfFramesWithoutDropping()
    {
        using var cts = new CancellationTokenSource(TestTimeoutMs);
        var port = GetFreeTcpPort();
        var server = new TcpServer(port, 100, 1024, false, 300, rateLimiter: null, logger: _logger);

        var expectedCount = 120;
        var receivedCount = 0;
        var completedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        server.OnMessageReceived += (ctx, data) =>
        {
            var current = Interlocked.Increment(ref receivedCount);
            if (current >= expectedCount)
            {
                completedTcs.TrySetResult(true);
            }

            return Task.CompletedTask;
        };

        _ = Task.Run(() => server.StartAsync(port), cts.Token);
        await Task.Delay(100, cts.Token);

        try
        {
            using var client = new TcpClient();
            await ConnectWithRetryAsync(client, IPAddress.Loopback, port, cts.Token);

            var stream = client.GetStream();
            for (var i = 0; i < expectedCount; i++)
            {
                var message = new Message
                {
                    Magic = ProtocolConstants.Magic,
                    VersionMajor = ProtocolConstants.VersionMajor,
                    VersionMinor = ProtocolConstants.VersionMinor,
                    Type = MessageType.Ping,
                    SequenceId = (ulong)(1000 + i),
                    Payload = new byte[] { (byte)(i % 256), 1, 2, 3 },
                    PayloadLength = 4,
                };

                var frame = new byte[Message.TotalSize(message)];
                MessageEncoder.Encode(message, frame);
                await stream.WriteAsync(frame, cts.Token);
            }

            await stream.FlushAsync(cts.Token);
            await completedTcs.Task.WaitAsync(TimeSpan.FromMilliseconds(2500), cts.Token);

            Assert.Equal(expectedCount, Volatile.Read(ref receivedCount));
        }
        finally
        {
            await server.StopAsync();
            cts.Cancel();
        }
    }

    private static async Task ConnectWithRetryAsync(TcpClient client, IPAddress address, int port, CancellationToken cancellationToken)
    {
        Exception? lastError = null;

        for (var attempt = 0; attempt < 20 && !cancellationToken.IsCancellationRequested; attempt++)
        {
            try
            {
                await client.ConnectAsync(address, port, cancellationToken);
                return;
            }
            catch (SocketException ex)
            {
                lastError = ex;
                await Task.Delay(50, cancellationToken);
            }
            catch (TaskCanceledException ex)
            {
                lastError = ex;
                break;
            }
        }

        if (lastError != null)
        {
            throw lastError;
        }

        throw new TimeoutException("Timed out waiting for TCP server to accept connections.");
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
        var server = new TcpServer(8080, 100, 2048, false, 300, rateLimiter: null, logger: _logger);

        // Assert
        Assert.Equal(8080, server.Port);
        Assert.Equal(100, server.MaxConnections);
        Assert.Equal(2048, server.BufferSize);
    }

    [Fact]
    public void TcpServer_SendAsync_WithNullContext_ShouldThrowException()
    {
        // Arrange
        var server = new TcpServer(8080, 100, 2048, false, 300, rateLimiter: null, logger: _logger);

        // Act & Assert
        Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await server.SendAsync(null!, new byte[] { 1, 2, 3 }));
    }

    [Fact]
    public async Task ConnectionContext_TryDropExpiredIncompleteFrame_ShouldDropOnlyAfterTimeout()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var context = new ConnectionContext(socket, 9001ul, 256);

        context.AppendIncomingData(new byte[] { 0x01, 0x02, 0x03, 0x04 });
        Assert.True(context.HasPendingIncomingData);

        Assert.False(context.TryDropExpiredIncompleteFrame(TimeSpan.FromMilliseconds(60), out _));

        await Task.Delay(80);

        Assert.True(context.TryDropExpiredIncompleteFrame(TimeSpan.FromMilliseconds(60), out var droppedBytes));
        Assert.Equal(4, droppedBytes);
        Assert.False(context.HasPendingIncomingData);
        Assert.Equal(1, context.IncompleteFrameDropCount);
    }

    [Fact]
    public void ConnectionContext_TransportMasking_ShouldRoundTripAcrossChunks()
    {
        using var sendSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        using var receiveSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var sender = new ConnectionContext(sendSocket, 111ul, 256);
        var receiver = new ConnectionContext(receiveSocket, 222ul, 256);

        var key = System.Text.Encoding.UTF8.GetBytes("mask-key-123");
        var part1Original = new byte[] { 10, 20, 30, 40, 50 };
        var part2Original = new byte[] { 60, 70, 80, 90 };

        var part1Masked = (byte[])part1Original.Clone();
        var part2Masked = (byte[])part2Original.Clone();

        sender.ApplyOutboundMaskInPlace(part1Masked, key);
        sender.ApplyOutboundMaskInPlace(part2Masked, key);

        receiver.ApplyInboundMaskInPlace(part1Masked, key);
        receiver.ApplyInboundMaskInPlace(part2Masked, key);

        Assert.Equal(part1Original, part1Masked);
        Assert.Equal(part2Original, part2Masked);
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

    private static byte[] ApplyMask(byte[] input, byte[] key, int offset)
    {
        if (key.Length == 0)
        {
            return input.ToArray();
        }

        var output = input.ToArray();
        for (var i = 0; i < output.Length; i++)
        {
            var keyIndex = (offset + i) % key.Length;
            output[i] ^= key[keyIndex];
        }

        return output;
    }
}
