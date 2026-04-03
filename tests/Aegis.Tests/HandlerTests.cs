using Xunit;
using System.Net.Sockets;
using System.Threading.Tasks;
using Aegis.Handlers;
using Aegis.Protocol;
using Aegis.Transport;
using Aegis.Common.Logging;
using Aegis.Crypto;
using Aegis.Common;
using Aegis.Data.Services;
using Aegis.Data.Repositories;
using Aegis.Data.Entities;
using Microsoft.Extensions.Logging;
using Moq;
using MessagePack;
using MessagePack.Resolvers;
using System.Text.Json;
using System.Text;
using System.Buffers.Binary;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace Aegis.Tests;

public class TestMessageSender : IMessageSender
{
    public List<ulong> SentConnectionIds { get; } = new();
    public List<byte[]> SentMessages { get; } = new();

    public Task SendMessageAsync(ulong connectionId, byte[] encryptedMessage)
    {
        SentConnectionIds.Add(connectionId);
        SentMessages.Add(encryptedMessage);
        return Task.CompletedTask;
    }

    public Task SendProtocolMessageAsync(ulong connectionId, ushort messageType, ulong sequenceId, byte[] payload, bool allowUnsigned = false)
    {
        var message = new Aegis.Protocol.Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = ProtocolConstants.VersionMajor,
            VersionMinor = ProtocolConstants.VersionMinor,
            Type = (MessageType)messageType,
            SequenceId = sequenceId,
            Payload = payload,
            PayloadLength = (uint)payload.Length,
        };

        var buffer = new byte[Aegis.Protocol.Message.TotalSize(message)];
        MessageEncoder.Encode(message, buffer);
        return SendMessageAsync(connectionId, buffer);
    }
}

public class HandlerTests
{
    private static readonly MessagePackSerializerOptions MsgPackOptions =
        MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);

    private readonly TestLogger _logger = new TestLogger();
    private readonly TestAntiSpamClient _antiSpam = new TestAntiSpamClient();
    private readonly TestMessageSender _messageSender = new TestMessageSender();
    private readonly AegisCryptoProvider _cryptoProvider = new AegisCryptoProvider();
    private readonly Mock<IMessageService> _messageServiceMock = new Mock<IMessageService>();
    private readonly SessionManager _sessionManager;

    public HandlerTests()
    {
        _sessionManager = new SessionManager(_cryptoProvider, _logger);
        _messageServiceMock
            .Setup(s => s.SendPrivateMessageAsync(It.IsAny<ulong>(), It.IsAny<ulong>(), It.IsAny<string>(), It.IsAny<MessageContentType>()))
            .ReturnsAsync((ulong fromUserId, ulong toUserId, string content, MessageContentType _) => new Aegis.Data.Entities.Message
            {
                Id = 100,
                FromUserId = fromUserId,
                ToUserId = toUserId,
                Content = content,
                CreatedAt = DateTime.UtcNow
            });
    }

            private static byte[] SerializePayload<T>(T payload) => MessagePackSerializer.Serialize(payload, MsgPackOptions);

    [Fact]
    public async Task MessageRouter_RegisterHandler_ShouldRouteCorrectly()
    {
        // Arrange
        var testHandler = new TestMessageHandler();
        var router = MessageRouter.ForHandlers(new IMessageHandler[] { testHandler }, _messageSender, _logger);
        var context = new TestConnectionContext(12345ul);
        var message = new Aegis.Protocol.Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = 1,
            VersionMinor = 0,
            Flags = 0,
            Type = MessageType.Message,
            SequenceId = 1,
            PayloadLength = 5,
            Payload = new byte[] { 1, 2, 3, 4, 5 },
        };

        // Act
        await router.RouteAsync(context, message);

        // Assert
        Assert.True(testHandler.Handled);
        Assert.Equal(message.SequenceId, testHandler.HandledSequenceId);
        Assert.Equal(context.ConnectionId, testHandler.HandledConnectionId);
    }

    [Fact]
    public async Task MessageRouter_UnknownMessage_ShouldSendError()
    {
        // Arrange
        var router = MessageRouter.ForHandlers(Array.Empty<IMessageHandler>(), _messageSender, _logger);
        var context = new TestConnectionContext(12345ul);
        var message = new Aegis.Protocol.Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = 1,
            VersionMinor = 0,
            Flags = 0,
            Type = (MessageType)999,
            SequenceId = 1,
            PayloadLength = 0,
            Payload = Array.Empty<byte>(),
        };

        // Act
        await router.RouteAsync(context, message);

        // Assert
        Assert.NotEmpty(_logger.ErrorMessages);
        Assert.Contains(_logger.ErrorMessages, m => m.Contains("Unknown message type"));
    }

    [Fact]
    public async Task AuthHandler_ShouldProcessAuthMessages()
    {
        // Arrange
        var handler = new AuthHandler(
            new Mock<IUserAuthenticationService>().Object,
            new RateLimiter(new Aegis.Common.Configuration.RateLimitOptions()),
            _sessionManager,
            _messageSender,
            new Mock<IMessageRepository>().Object,
            new Mock<IUserSearchService>().Object,
            new Mock<Microsoft.Extensions.Logging.ILogger<AuthHandler>>().Object);
        var context = new TestConnectionContext(12345ul);
        var message = new Aegis.Protocol.Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = 1,
            VersionMinor = 0,
            Flags = 0,
            Type = MessageType.Auth,
            SequenceId = 1,
            PayloadLength = 10,
            Payload = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 },
        };

        // Act
        await handler.HandleAsync(context, message);

        // Assert - with invalid JSON payload, handler should send error response
        Assert.NotEmpty(_messageSender.SentMessages);
    }

    [Fact]
    public async Task PingHandler_ShouldUpdateActivity()
    {
        // Arrange
        var handler = new PingHandler();
        var context = new TestConnectionContext(12345ul);
        var initialActivity = context.LastActivity;

        var message = new Aegis.Protocol.Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = 1,
            VersionMinor = 0,
            Flags = 0,
            Type = MessageType.Ping,
            SequenceId = 1,
            PayloadLength = 8,
            Payload = BitConverter.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
        };

        // Act
        await handler.HandleAsync(context, message);

        // Assert
        Assert.True(context.LastActivity > initialActivity,
            $"LastActivity ({context.LastActivity}) should be greater than initial ({initialActivity})");
    }

    [Fact]
    public async Task HandshakeHandler_StrictV2Only_ShouldRejectLegacyHandshakePayload()
    {
        var protocolOptions = Options.Create(new Aegis.Common.Configuration.ProtocolSecurityOptions
        {
            EnableV2Handshake = true,
            AllowLegacyHandshakeFallback = false,
            RequireAppCredentials = false,
        });

        var handler = new HandshakeHandler(
            _sessionManager,
            _messageSender,
            _cryptoProvider,
            protocolOptions,
            new Mock<ILogger<HandshakeHandler>>().Object,
            null,
            null);

        var context = new TestConnectionContext(777001ul);
        _sessionManager.CreateSession(context.ConnectionId);

        using var clientKey = EcdhKeyExchange.GenerateKeyPair();
        var legacyPayload = SerializePayload(new HandshakeRequestPayload(
            PublicKey: Convert.ToBase64String(clientKey.PublicKeyRaw),
            ClientVersion: 1000));

        var message = new Aegis.Protocol.Message
        {
            Type = MessageType.Handshake,
            SequenceId = 7001,
            Payload = legacyPayload,
            PayloadLength = (uint)legacyPayload.Length,
        };

        await handler.HandleAsync(context, message);

        Assert.NotEmpty(_messageSender.SentMessages);
        var wire = MessageEncoder.Decode(_messageSender.SentMessages[^1]);
        var response = MessagePackSerializer.Deserialize<HandshakeResponsePayload>(wire.Payload, MsgPackOptions);

        Assert.False(response.Success);
        Assert.Equal("V2 handshake required", response.Message);
        Assert.False(_sessionManager.GetSession(context.ConnectionId)?.HandshakeEstablished ?? true);
    }

    [Fact]
    public async Task HandshakeHandler_V2Flow_ShouldEstablishSessionWhenProofIsValid()
    {
        var protocolOptions = Options.Create(new Aegis.Common.Configuration.ProtocolSecurityOptions
        {
            EnableV2Handshake = true,
            AllowLegacyHandshakeFallback = false,
            RequireAppCredentials = false,
            V2HandshakeClockSkewMs = 90_000,
            V2HandshakeCookieTtlMs = 60_000,
            V2ReplayWindowSeconds = 120,
        });

        var handler = new HandshakeHandler(
            _sessionManager,
            _messageSender,
            _cryptoProvider,
            protocolOptions,
            new Mock<ILogger<HandshakeHandler>>().Object,
            null,
            null);

        var context = new TestConnectionContext(777002ul);
        _sessionManager.CreateSession(context.ConnectionId);

        using var clientKey = EcdhKeyExchange.GenerateKeyPair();
        var clientNonce = new byte[32];
        RandomNumberGenerator.Fill(clientNonce);

        var helloEnvelope = new HandshakeV2Envelope(
            Stage: "client_hello_v2",
            ClientHello: new ClientHelloV2(
                ApiId: 0,
                AppHash: string.Empty,
                ClientEphemeralPublicKey: clientKey.PublicKeyRaw,
                ClientNonce: clientNonce,
                ClientUnixTimeMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                TransportHint: "obfs+tls"));

        var helloPayload = SerializePayload(helloEnvelope);
        var helloMessage = new Aegis.Protocol.Message
        {
            Type = MessageType.Handshake,
            SequenceId = 7002,
            Payload = helloPayload,
            PayloadLength = (uint)helloPayload.Length,
        };

        await handler.HandleAsync(context, helloMessage);

        Assert.NotEmpty(_messageSender.SentMessages);
        var serverHelloWire = MessageEncoder.Decode(_messageSender.SentMessages[^1]);
        var serverHelloEnvelope = MessagePackSerializer.Deserialize<HandshakeV2ResponseEnvelope>(serverHelloWire.Payload, MsgPackOptions);

        Assert.True(serverHelloEnvelope.Success);
        Assert.Equal("server_hello_v2", serverHelloEnvelope.Stage);
        Assert.NotNull(serverHelloEnvelope.ServerHello);

        var serverHello = serverHelloEnvelope.ServerHello!;
        var sharedSecret = clientKey.ComputeSharedSecret(serverHello.ServerEphemeralPublicKey);
        var transcriptHash = SHA256.HashData(helloPayload);
        var keys = V2KeySchedule.DeriveSessionKeys(sharedSecret, clientNonce, serverHello.ServerNonce, transcriptHash);
        var proof = V2KeySchedule.ComputeClientFinishProof(keys.AckKey, transcriptHash);

        var finishEnvelope = new HandshakeV2Envelope(
            Stage: "client_finish_v2",
            ClientFinish: new ClientFinishV2(serverHello.Cookie, proof));
        var finishPayload = SerializePayload(finishEnvelope);
        var finishMessage = new Aegis.Protocol.Message
        {
            Type = MessageType.Handshake,
            SequenceId = 7003,
            Payload = finishPayload,
            PayloadLength = (uint)finishPayload.Length,
        };

        await handler.HandleAsync(context, finishMessage);

        Assert.True(_messageSender.SentMessages.Count >= 2);
        var finishWire = MessageEncoder.Decode(_messageSender.SentMessages[^1]);
        var finishResponse = MessagePackSerializer.Deserialize<HandshakeV2ResponseEnvelope>(finishWire.Payload, MsgPackOptions);

        Assert.True(finishResponse.Success);
        Assert.Equal("server_finish_v2", finishResponse.Stage);
        Assert.True(_sessionManager.GetSession(context.ConnectionId)?.HandshakeEstablished ?? false);
    }

    [Fact]
    public async Task HandshakeHandler_V2Flow_ShouldRejectInvalidFinishProof()
    {
        var protocolOptions = Options.Create(new Aegis.Common.Configuration.ProtocolSecurityOptions
        {
            EnableV2Handshake = true,
            AllowLegacyHandshakeFallback = false,
            RequireAppCredentials = false,
            V2HandshakeClockSkewMs = 90_000,
            V2HandshakeCookieTtlMs = 60_000,
            V2ReplayWindowSeconds = 120,
        });

        var handler = new HandshakeHandler(
            _sessionManager,
            _messageSender,
            _cryptoProvider,
            protocolOptions,
            new Mock<ILogger<HandshakeHandler>>().Object,
            null,
            null);

        var context = new TestConnectionContext(777003ul);
        _sessionManager.CreateSession(context.ConnectionId);

        using var clientKey = EcdhKeyExchange.GenerateKeyPair();
        var clientNonce = new byte[32];
        RandomNumberGenerator.Fill(clientNonce);

        var helloEnvelope = new HandshakeV2Envelope(
            Stage: "client_hello_v2",
            ClientHello: new ClientHelloV2(
                ApiId: 0,
                AppHash: string.Empty,
                ClientEphemeralPublicKey: clientKey.PublicKeyRaw,
                ClientNonce: clientNonce,
                ClientUnixTimeMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                TransportHint: "obfs+tls"));

        var helloPayload = SerializePayload(helloEnvelope);
        var helloMessage = new Aegis.Protocol.Message
        {
            Type = MessageType.Handshake,
            SequenceId = 7004,
            Payload = helloPayload,
            PayloadLength = (uint)helloPayload.Length,
        };

        await handler.HandleAsync(context, helloMessage);

        var serverHelloWire = MessageEncoder.Decode(_messageSender.SentMessages[^1]);
        var serverHelloEnvelope = MessagePackSerializer.Deserialize<HandshakeV2ResponseEnvelope>(serverHelloWire.Payload, MsgPackOptions);

        Assert.True(serverHelloEnvelope.Success);
        Assert.Equal("server_hello_v2", serverHelloEnvelope.Stage);
        Assert.NotNull(serverHelloEnvelope.ServerHello);

        var serverHello = serverHelloEnvelope.ServerHello!;
        var sharedSecret = clientKey.ComputeSharedSecret(serverHello.ServerEphemeralPublicKey);
        var transcriptHash = SHA256.HashData(helloPayload);
        var keys = V2KeySchedule.DeriveSessionKeys(sharedSecret, clientNonce, serverHello.ServerNonce, transcriptHash);
        var proof = V2KeySchedule.ComputeClientFinishProof(keys.AckKey, transcriptHash);
        proof[0] ^= 0x01;

        var finishEnvelope = new HandshakeV2Envelope(
            Stage: "client_finish_v2",
            ClientFinish: new ClientFinishV2(serverHello.Cookie, proof));
        var finishPayload = SerializePayload(finishEnvelope);
        var finishMessage = new Aegis.Protocol.Message
        {
            Type = MessageType.Handshake,
            SequenceId = 7005,
            Payload = finishPayload,
            PayloadLength = (uint)finishPayload.Length,
        };

        await handler.HandleAsync(context, finishMessage);

        var finishWire = MessageEncoder.Decode(_messageSender.SentMessages[^1]);
        var finishResponse = MessagePackSerializer.Deserialize<HandshakeV2ResponseEnvelope>(finishWire.Payload, MsgPackOptions);

        Assert.False(finishResponse.Success);
        Assert.Equal("server_error_v2", finishResponse.Stage);
        Assert.Equal("Invalid handshake proof", finishResponse.Message);
        Assert.False(_sessionManager.GetSession(context.ConnectionId)?.HandshakeEstablished ?? true);
    }

    [Fact]
    public async Task HandshakeHandler_V2Flow_ShouldRejectReplayedClientNonce()
    {
        var protocolOptions = Options.Create(new Aegis.Common.Configuration.ProtocolSecurityOptions
        {
            EnableV2Handshake = true,
            AllowLegacyHandshakeFallback = false,
            RequireAppCredentials = false,
            V2HandshakeClockSkewMs = 90_000,
            V2HandshakeCookieTtlMs = 60_000,
            V2ReplayWindowSeconds = 120,
        });

        var handler = new HandshakeHandler(
            _sessionManager,
            _messageSender,
            _cryptoProvider,
            protocolOptions,
            new Mock<ILogger<HandshakeHandler>>().Object,
            null,
            null);

        var context = new TestConnectionContext(777004ul);
        _sessionManager.CreateSession(context.ConnectionId);

        using var clientKey = EcdhKeyExchange.GenerateKeyPair();
        var replayedNonce = new byte[32];
        RandomNumberGenerator.Fill(replayedNonce);

        var helloEnvelope = new HandshakeV2Envelope(
            Stage: "client_hello_v2",
            ClientHello: new ClientHelloV2(
                ApiId: 0,
                AppHash: string.Empty,
                ClientEphemeralPublicKey: clientKey.PublicKeyRaw,
                ClientNonce: replayedNonce,
                ClientUnixTimeMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                TransportHint: "obfs+tls"));

        var firstHelloPayload = SerializePayload(helloEnvelope);
        var firstHelloMessage = new Aegis.Protocol.Message
        {
            Type = MessageType.Handshake,
            SequenceId = 7006,
            Payload = firstHelloPayload,
            PayloadLength = (uint)firstHelloPayload.Length,
        };

        await handler.HandleAsync(context, firstHelloMessage);
        var firstWire = MessageEncoder.Decode(_messageSender.SentMessages[^1]);
        var firstResponse = MessagePackSerializer.Deserialize<HandshakeV2ResponseEnvelope>(firstWire.Payload, MsgPackOptions);

        Assert.True(firstResponse.Success);
        Assert.Equal("server_hello_v2", firstResponse.Stage);

        var secondHelloPayload = SerializePayload(helloEnvelope);
        var secondHelloMessage = new Aegis.Protocol.Message
        {
            Type = MessageType.Handshake,
            SequenceId = 7007,
            Payload = secondHelloPayload,
            PayloadLength = (uint)secondHelloPayload.Length,
        };

        await handler.HandleAsync(context, secondHelloMessage);

        var secondWire = MessageEncoder.Decode(_messageSender.SentMessages[^1]);
        var secondResponse = MessagePackSerializer.Deserialize<HandshakeV2ResponseEnvelope>(secondWire.Payload, MsgPackOptions);

        Assert.False(secondResponse.Success);
        Assert.Equal("server_error_v2", secondResponse.Stage);
        Assert.Equal("Replay detected", secondResponse.Message);
    }

    [Fact]
    public async Task HandshakeHandler_V2Flow_ShouldRejectExpiredCookie()
    {
        var protocolOptions = Options.Create(new Aegis.Common.Configuration.ProtocolSecurityOptions
        {
            EnableV2Handshake = true,
            AllowLegacyHandshakeFallback = false,
            RequireAppCredentials = false,
            V2HandshakeClockSkewMs = 90_000,
            V2HandshakeCookieTtlMs = -1,
            V2ReplayWindowSeconds = 120,
        });

        var handler = new HandshakeHandler(
            _sessionManager,
            _messageSender,
            _cryptoProvider,
            protocolOptions,
            new Mock<ILogger<HandshakeHandler>>().Object,
            null,
            null);

        var context = new TestConnectionContext(777005ul);
        _sessionManager.CreateSession(context.ConnectionId);

        using var clientKey = EcdhKeyExchange.GenerateKeyPair();
        var clientNonce = new byte[32];
        RandomNumberGenerator.Fill(clientNonce);

        var helloEnvelope = new HandshakeV2Envelope(
            Stage: "client_hello_v2",
            ClientHello: new ClientHelloV2(
                ApiId: 0,
                AppHash: string.Empty,
                ClientEphemeralPublicKey: clientKey.PublicKeyRaw,
                ClientNonce: clientNonce,
                ClientUnixTimeMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                TransportHint: "obfs+tls"));

        var helloPayload = SerializePayload(helloEnvelope);
        var helloMessage = new Aegis.Protocol.Message
        {
            Type = MessageType.Handshake,
            SequenceId = 7008,
            Payload = helloPayload,
            PayloadLength = (uint)helloPayload.Length,
        };

        await handler.HandleAsync(context, helloMessage);

        var serverHelloWire = MessageEncoder.Decode(_messageSender.SentMessages[^1]);
        var serverHelloEnvelope = MessagePackSerializer.Deserialize<HandshakeV2ResponseEnvelope>(serverHelloWire.Payload, MsgPackOptions);
        Assert.True(serverHelloEnvelope.Success);
        Assert.Equal("server_hello_v2", serverHelloEnvelope.Stage);
        Assert.NotNull(serverHelloEnvelope.ServerHello);

        var serverHello = serverHelloEnvelope.ServerHello!;
        var sharedSecret = clientKey.ComputeSharedSecret(serverHello.ServerEphemeralPublicKey);
        var transcriptHash = SHA256.HashData(helloPayload);
        var keys = V2KeySchedule.DeriveSessionKeys(sharedSecret, clientNonce, serverHello.ServerNonce, transcriptHash);
        var proof = V2KeySchedule.ComputeClientFinishProof(keys.AckKey, transcriptHash);

        var finishEnvelope = new HandshakeV2Envelope(
            Stage: "client_finish_v2",
            ClientFinish: new ClientFinishV2(serverHello.Cookie, proof));
        var finishPayload = SerializePayload(finishEnvelope);
        var finishMessage = new Aegis.Protocol.Message
        {
            Type = MessageType.Handshake,
            SequenceId = 7009,
            Payload = finishPayload,
            PayloadLength = (uint)finishPayload.Length,
        };

        await handler.HandleAsync(context, finishMessage);

        var finishWire = MessageEncoder.Decode(_messageSender.SentMessages[^1]);
        var finishResponse = MessagePackSerializer.Deserialize<HandshakeV2ResponseEnvelope>(finishWire.Payload, MsgPackOptions);

        Assert.False(finishResponse.Success);
        Assert.Equal("server_error_v2", finishResponse.Stage);
        Assert.Equal("Handshake cookie expired", finishResponse.Message);
        Assert.False(_sessionManager.GetSession(context.ConnectionId)?.HandshakeEstablished ?? true);
    }

    [Fact]
    public async Task UserPresenceHandler_ShouldAcceptIsoTimestampPayloadFromDart()
    {
        var userRepository = new Mock<IUserRepository>();
        var sessionRepository = new Mock<ISessionRepository>();
        var logger = new Mock<Microsoft.Extensions.Logging.ILogger<UserPresenceHandler>>();
        var handler = new UserPresenceHandler(_sessionManager, userRepository.Object, sessionRepository.Object, logger.Object);
        var context = new TestConnectionContext(9001ul);

        _sessionManager.CreateSession(context.ConnectionId);
        _sessionManager.EstablishHandshake(context.ConnectionId, new byte[32]);
        _sessionManager.AuthenticateSession(context.ConnectionId, 42ul, "tester");

        var user = new User { Id = 42ul, Username = "tester" };
        var dbSession = new Session { Id = 7ul, UserId = 42ul, ConnectionId = context.ConnectionId.ToString(), IsActive = true };

        userRepository.Setup(x => x.GetByIdAsync(42ul)).ReturnsAsync(user);
        userRepository.Setup(x => x.UpdateAsync(It.IsAny<User>())).ReturnsAsync((User updated) => updated);
        sessionRepository.Setup(x => x.GetByConnectionIdAsync(context.ConnectionId.ToString())).ReturnsAsync(dbSession);
        sessionRepository.Setup(x => x.UpdateAsync(It.IsAny<Session>())).ReturnsAsync((Session updated) => updated);

        var payload = SerializePayload(new Dictionary<string, object?>
        {
            ["IsOnline"] = false,
            ["ClientTimestamp"] = "2026-04-01T09:30:46.766Z"
        });

        var message = new Aegis.Protocol.Message
        {
            Type = MessageType.UserPresence,
            SequenceId = 3,
            Payload = payload,
            PayloadLength = (uint)payload.Length
        };

        await handler.HandleAsync(context, message);

        Assert.False(_sessionManager.IsUserOnline(42ul));
        userRepository.Verify(x => x.UpdateAsync(It.Is<User>(u => u.Id == 42ul && u.LastSeenAt.HasValue)), Times.Once);
        sessionRepository.Verify(x => x.UpdateAsync(It.Is<Session>(s => s.Id == 7ul && !s.IsActive && s.LastActivityAt.HasValue)), Times.Once);
    }

    [Fact]
    public async Task MessageReactHandler_ShouldResolveChannelMembersFromMessageChannel()
    {
        var channelRepository = new Mock<IChannelRepository>();
        var groupRepository = new Mock<IGroupRepository>();
        var reactionRepository = new Mock<IReactionRepository>();
        var logger = new Mock<Microsoft.Extensions.Logging.ILogger<MessageReactHandler>>();
        var handler = new MessageReactHandler(
            _sessionManager,
            _messageSender,
            channelRepository.Object,
            groupRepository.Object,
            reactionRepository.Object,
            logger.Object);

        var actorContext = new TestConnectionContext(9101ul);
        _sessionManager.CreateSession(actorContext.ConnectionId);
        _sessionManager.EstablishHandshake(actorContext.ConnectionId, new byte[32]);
        _sessionManager.AuthenticateSession(actorContext.ConnectionId, 1ul, "actor");

        _sessionManager.CreateSession(9102ul);
        _sessionManager.EstablishHandshake(9102ul, new byte[32]);
        _sessionManager.AuthenticateSession(9102ul, 2ul, "recipient");

        channelRepository
            .Setup(x => x.GetChannelMessageAsync(555ul))
            .ReturnsAsync(new ChannelMessage { Id = 555ul, ChannelId = 99ul, FromUserId = 1ul, Content = "hello" });
        channelRepository
            .Setup(x => x.GetChannelMembersAsync(99ul))
            .ReturnsAsync(new[]
            {
                new ChannelMember { ChannelId = 99ul, UserId = 1ul, IsActive = true },
                new ChannelMember { ChannelId = 99ul, UserId = 2ul, IsActive = true }
            });
        reactionRepository
            .Setup(x => x.AddAsync("channel", 555ul, 1ul, "🔥"))
            .ReturnsAsync(new MessageReaction { Id = 1ul, Scope = "channel", MessageId = 555ul, UserId = 1ul, Emoji = "🔥" });
        reactionRepository
            .Setup(x => x.GetByMessageAsync("channel", 555ul))
            .ReturnsAsync(new[]
            {
                new MessageReaction { Id = 1ul, Scope = "channel", MessageId = 555ul, UserId = 1ul, Emoji = "🔥" }
            });

        var payload = SerializePayload(new MessageReactRequest("channel", 555ul, "🔥", false));
        var message = new Aegis.Protocol.Message
        {
            Type = MessageType.MessageReact,
            SequenceId = 77,
            Payload = payload,
            PayloadLength = (uint)payload.Length
        };

        await handler.HandleAsync(actorContext, message);

        channelRepository.Verify(x => x.GetChannelMembersAsync(99ul), Times.Once);
        channelRepository.Verify(x => x.GetChannelMembersAsync(555ul), Times.Never);
        Assert.Contains(9102ul, _messageSender.SentConnectionIds);
    }

    [Fact]
    public async Task MessageHandler_AllowedMessage_ShouldSendAck()
    {
        // Arrange
        var handler = new MessageHandler(_antiSpam, _messageServiceMock.Object, _messageSender, _sessionManager, _logger);
        var context = new TestConnectionContext(12345ul);
        var recipientContext = new TestConnectionContext(54321ul);
        _sessionManager.CreateSession(context.ConnectionId);
        _sessionManager.EstablishHandshake(context.ConnectionId, new byte[32]);
        _sessionManager.AuthenticateSession(context.ConnectionId, 42, "tester");
        _sessionManager.CreateSession(recipientContext.ConnectionId);
        _sessionManager.EstablishHandshake(recipientContext.ConnectionId, new byte[32]);
        _sessionManager.AuthenticateSession(recipientContext.ConnectionId, 24, "recipient");

        var payload = Encoding.UTF8.GetBytes("{\"recipientId\":24,\"content\":\"Hello World!\"}");
        var message = new Aegis.Protocol.Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = 1,
            VersionMinor = 0,
            Flags = 0,
            Type = MessageType.Message,
            SequenceId = 1,
            PayloadLength = (uint)payload.Length,
            Payload = payload,
        };

        _antiSpam.AllowNextMessage = true;

        // Act
        await handler.HandleAsync(context, message);

        // Assert
        Assert.True(_antiSpam.CheckedConnection);
        Assert.True(handler.AckSent);
        Assert.Equal(message.SequenceId, handler.AckSequenceId);
    }

    [Fact]
    public async Task MessageHandler_RejectedMessage_ShouldSendError()
    {
        // Arrange
        var handler = new MessageHandler(_antiSpam, _messageServiceMock.Object, _messageSender, _sessionManager, _logger);
        var context = new TestConnectionContext(12345ul);
        _sessionManager.CreateSession(context.ConnectionId);
        _sessionManager.EstablishHandshake(context.ConnectionId, new byte[32]);
        _sessionManager.AuthenticateSession(context.ConnectionId, 42, "tester");

        var payload = Encoding.UTF8.GetBytes("{\"recipientId\":24,\"content\":\"Spam message\"}");
        var message = new Aegis.Protocol.Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = 1,
            VersionMinor = 0,
            Flags = 0,
            Type = MessageType.Message,
            SequenceId = 1,
            PayloadLength = (uint)payload.Length,
            Payload = payload,
        };

        _antiSpam.AllowNextMessage = false;

        // Act
        await handler.HandleAsync(context, message);

        // Assert
        Assert.True(_antiSpam.CheckedConnection);
        Assert.Contains("rejected by anti-spam", handler.ErrorMessage);
    }

    [Fact]
    public void TypingIndicatorStore_ShouldThrottleRapidTypingBursts()
    {
        using var store = new TypingIndicatorStore();

        var first = store.ShouldBroadcast(11, "private", 42, isTyping: true);
        var second = store.ShouldBroadcast(11, "private", 42, isTyping: true);
        var stop = store.ShouldBroadcast(11, "private", 42, isTyping: false);

        Assert.True(first);
        Assert.False(second);
        Assert.True(stop);
    }

    [Fact]
    public async Task MessageHandler_ShouldPersistAndAdvanceSignalChainState()
    {
        var chainRepo = new InMemorySignalChainStateRepository();
        var handler = new MessageHandler(_antiSpam, _messageServiceMock.Object, _messageSender, _sessionManager, _logger, chainRepo);
        var context = new TestConnectionContext(8881ul);
        var recipientContext = new TestConnectionContext(8882ul);

        _sessionManager.CreateSession(context.ConnectionId);
        _sessionManager.EstablishHandshake(context.ConnectionId, new byte[32]);
        _sessionManager.AuthenticateSession(context.ConnectionId, 71, "sender");

        _sessionManager.CreateSession(recipientContext.ConnectionId);
        _sessionManager.EstablishHandshake(recipientContext.ConnectionId, new byte[32]);
        _sessionManager.AuthenticateSession(recipientContext.ConnectionId, 72, "recipient");

        var payload = BuildLegacyDirectPayload(72, "signal message");
        var message = new Aegis.Protocol.Message
        {
            Type = MessageType.Message,
            SequenceId = 301,
            Payload = payload,
            PayloadLength = (uint)payload.Length,
        };

        _antiSpam.AllowNextMessage = true;

        await handler.HandleAsync(context, message);

        var state = await chainRepo.GetOrCreateAsync(71, 72);
        Assert.Equal(1u, state.NextSendingMessageNumber);
        Assert.False(string.IsNullOrWhiteSpace(state.LastMessageKeyHash));
    }

    [Fact]
    public async Task FileDownloadRateLimiter_ShouldDelayWhenWindowExceeded()
    {
        var limiter = new FileDownloadRateLimiter(bytesPerSecond: 1024);
        await limiter.WaitForBudgetAsync(91, 50 * 1024);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await limiter.WaitForBudgetAsync(91, 50 * 1024);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds >= 600, $"Expected limiter delay, got {sw.ElapsedMilliseconds}ms");
    }

    private class TestConnectionContext : ConnectionContext
    {
        private DateTime _lastActivity = DateTime.UtcNow;

        public TestConnectionContext(ulong connectionId) : base(new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp), connectionId)
        {
        }

        public new DateTime LastActivity
        {
            get => _lastActivity;
            private set => _lastActivity = value;
        }

        public override void UpdateActivity()
        {
            _lastActivity = DateTime.UtcNow;
        }
    }

    private class TestMessageHandler : IMessageHandler
    {
        public MessageType Type => MessageType.Message;

        public bool Handled { get; private set; }
        public ulong HandledConnectionId { get; private set; }
        public ulong HandledSequenceId { get; private set; }

        public ValueTask HandleAsync(ConnectionContext context, Aegis.Protocol.Message message)
        {
            Handled = true;
            HandledConnectionId = context.ConnectionId;
            HandledSequenceId = message.SequenceId;
            return ValueTask.CompletedTask;
        }
    }

    private class TestAntiSpamClient : IAntiSpamClient
    {
        public bool AllowNextMessage { get; set; } = true;
        public bool CheckedConnection { get; private set; }
        public ulong CheckedConnectionId { get; private set; }

        public async Task<bool> CheckMessageAsync(ulong connectionId, byte[] message)
        {
            CheckedConnection = true;
            CheckedConnectionId = connectionId;
            await Task.CompletedTask;
            return AllowNextMessage;
        }
    }

    private sealed class InMemorySignalChainStateRepository : ISignalChainStateRepository
    {
        private readonly Dictionary<(ulong Owner, ulong Peer), SignalChainState> _states = new();

        public Task<SignalChainState> GetOrCreateAsync(ulong ownerUserId, ulong peerUserId)
        {
            if (_states.TryGetValue((ownerUserId, peerUserId), out var existing))
            {
                return Task.FromResult(existing);
            }

            var state = new SignalChainState
            {
                OwnerUserId = ownerUserId,
                PeerUserId = peerUserId,
                RootKeyBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                SendingChainKeyBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                ReceivingChainKeyBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                NextSendingMessageNumber = 0,
                NextReceivingMessageNumber = 0,
                UpdatedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
            };

            _states[(ownerUserId, peerUserId)] = state;
            return Task.FromResult(state);
        }

        public Task<SignalChainState> UpdateAsync(SignalChainState state)
        {
            state.UpdatedAt = DateTime.UtcNow;
            _states[(state.OwnerUserId, state.PeerUserId)] = state;
            return Task.FromResult(state);
        }
    }

    private static byte[] BuildLegacyDirectPayload(ulong recipientId, string content)
    {
        var contentBytes = Encoding.UTF8.GetBytes(content);
        var payload = new byte[20 + contentBytes.Length];
        BinaryPrimitives.WriteUInt64BigEndian(payload.AsSpan(8, 8), recipientId);
        contentBytes.CopyTo(payload.AsSpan(20));
        return payload;
    }

    private class TestLogger : Aegis.Common.Logging.ILogger
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

    // New tests for updated handlers
    [Fact]
    public async Task MessageRouter_ShouldRouteNewMessageTypes()
    {
        // Arrange
        var mockRegistrationService = new Mock<IUserRegistrationService>();
        var mockSearchService = new Mock<IUserSearchService>();
        var rateLimiter = new RateLimiter(new Aegis.Common.Configuration.RateLimitOptions());
        var domainRules = new DomainRulesAdapter(new Aegis.DomainRules.MessageDomainRules());

        var registrationHandler = new RegistrationHandler(mockRegistrationService.Object, _messageSender, rateLimiter, new Mock<ILogger<RegistrationHandler>>().Object);
        var searchHandler = new UserSearchHandler(mockSearchService.Object, _sessionManager, _messageSender, rateLimiter, new Mock<ILogger<UserSearchHandler>>().Object);
        var channelHandler = new ChannelMessageHandler(
            new Mock<IMessageService>().Object,
            new Mock<IChannelRepository>().Object,
            _sessionManager,
            _messageSender,
            domainRules,
            new Mock<Microsoft.Extensions.Logging.ILogger<ChannelMessageHandler>>().Object);

        var router = MessageRouter.ForHandlers(new IMessageHandler[] { registrationHandler, searchHandler, channelHandler }, _messageSender, _logger);
        var context = new TestConnectionContext(12345ul);
        _sessionManager.CreateSession(context.ConnectionId);
        _sessionManager.EstablishHandshake(context.ConnectionId, new byte[32]);
        _sessionManager.AuthenticateSession(context.ConnectionId, 1, "router-user");

        // Test Registration message routing
        var registrationRequest = new RegistrationRequest("testuser", "test@example.com", "password123", "public_key");
        var registrationPayload = JsonSerializer.SerializeToUtf8Bytes(registrationRequest);
        var registrationMessage = new Aegis.Protocol.Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = 1,
            VersionMinor = 0,
            Type = MessageType.Register,
            SequenceId = 1,
            PayloadLength = (uint)registrationPayload.Length,
            Payload = registrationPayload
        };

        // Act & Assert - Should not throw
        await router.RouteAsync(context, registrationMessage);

        // Test UserSearch message routing
        var searchRequest = new UserSearchRequest("test", 10);
        var searchPayload = JsonSerializer.SerializeToUtf8Bytes(searchRequest);
        var searchMessage = new Aegis.Protocol.Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = 1,
            VersionMinor = 0,
            Type = MessageType.UserSearch,
            SequenceId = 2,
            PayloadLength = (uint)searchPayload.Length,
            Payload = searchPayload
        };

        // Act & Assert - Should not throw
        await router.RouteAsync(context, searchMessage);

        // Test ChannelMessage message routing
        var channelRequest = new ChannelMessageRequest(1, "Hello, channel!", MessageContentType.Text);
        var channelPayload = JsonSerializer.SerializeToUtf8Bytes(channelRequest);
        var channelMessage = new Aegis.Protocol.Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = 1,
            VersionMinor = 0,
            Type = MessageType.ChannelMessage,
            SequenceId = 3,
            PayloadLength = (uint)channelPayload.Length,
            Payload = channelPayload
        };

        // Act & Assert - Should not throw
        await router.RouteAsync(context, channelMessage);
    }

    [Fact]
    public void NewHandlers_ShouldHaveCorrectMessageTypes()
    {
        // Arrange
        var mockRegistrationService = new Mock<IUserRegistrationService>();
        var mockSearchService = new Mock<IUserSearchService>();
        var rateLimiter = new RateLimiter(new Aegis.Common.Configuration.RateLimitOptions());
        var domainRules = new DomainRulesAdapter(new Aegis.DomainRules.MessageDomainRules());

        var registrationHandler = new RegistrationHandler(mockRegistrationService.Object, _messageSender, rateLimiter, new Mock<ILogger<RegistrationHandler>>().Object);
        var searchHandler = new UserSearchHandler(mockSearchService.Object, _sessionManager, _messageSender, rateLimiter, new Mock<ILogger<UserSearchHandler>>().Object);
        var channelHandler = new ChannelMessageHandler(
            new Mock<IMessageService>().Object,
            new Mock<IChannelRepository>().Object,
            _sessionManager,
            _messageSender,
            domainRules,
            new Mock<Microsoft.Extensions.Logging.ILogger<ChannelMessageHandler>>().Object);
        var channelCreateHandler = new ChannelCreateHandler(
            new Mock<IChannelService>().Object,
            _sessionManager,
            _messageSender,
            new Mock<Microsoft.Extensions.Logging.ILogger<ChannelCreateHandler>>().Object);
        var privateChatHandler = new PrivateChatMessageHandler(
            new Mock<IMessageService>().Object,
            mockSearchService.Object,
            new Mock<IBotManagementService>().Object,
            _sessionManager,
            _messageSender,
            domainRules,
            new Mock<Microsoft.Extensions.Logging.ILogger<PrivateChatMessageHandler>>().Object);

        // Act & Assert
        Assert.Equal(MessageType.Register, registrationHandler.Type);
        Assert.Equal(MessageType.UserSearch, searchHandler.Type);
        Assert.Equal(MessageType.ChannelMessage, channelHandler.Type);
        Assert.Equal(MessageType.ChannelCreate, channelCreateHandler.Type);
        Assert.Equal(MessageType.PrivateChatMessage, privateChatHandler.Type);
    }

    [Fact]
    public async Task MessageRouter_ShouldHandleUnknownMessageType()
    {
        // Arrange
        var testHandler = new TestMessageHandler();
        var router = MessageRouter.ForHandlers(new IMessageHandler[] { testHandler }, _messageSender, _logger);
        var context = new TestConnectionContext(12345ul);
        var message = new Aegis.Protocol.Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = 1,
            VersionMinor = 0,
            Type = (MessageType)999, // Unknown type
            SequenceId = 1,
            PayloadLength = 0,
            Payload = Array.Empty<byte>()
        };

        // Act & Assert - Should not throw, should handle gracefully
        await router.RouteAsync(context, message);

        // Should log error about unknown message type
        Assert.NotEmpty(_logger.ErrorMessages);
        Assert.Contains("Unknown message type", _logger.ErrorMessages.First());
    }
}
