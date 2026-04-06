using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Aegis.Common;
using Aegis.Common.Configuration;
using Aegis.Crypto;
using Aegis.Data;
using Aegis.Data.Entities;
using Aegis.Data.Repositories;
using Aegis.Data.Services;
using Aegis.Handlers;
using Aegis.Protocol;
using Aegis.Transport;
using DataMessage = Aegis.Data.Entities.Message;
using ProtocolMessage = Aegis.Protocol.Message;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Aegis.Tests;

public sealed class ServerIntegrationTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;

    public ServerIntegrationTests()
    {
        var services = new ServiceCollection();

        services.AddDbContext<AegisDbContext>(options =>
            options.UseInMemoryDatabase($"aegis-integration-{Guid.NewGuid():N}"));

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IChannelRepository, ChannelRepository>();
        services.AddScoped<IPrivateChatRepository, PrivateChatRepository>();
        services.AddScoped<IGroupRepository, GroupRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();

        services.AddScoped<Aegis.Data.Utils.FastIdGenerator>(_ => new Aegis.Data.Utils.FastIdGenerator(1));
        services.AddScoped<IUserRegistrationService, UserRegistrationService>();
        services.AddScoped<IUserAuthenticationService, UserAuthenticationService>();
        services.AddScoped<IUserTwoFactorService>(_ =>
        {
            var twoFactor = new Mock<IUserTwoFactorService>();
            twoFactor.Setup(x => x.ValidateAsync(It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>()))
                .ReturnsAsync(true);
            twoFactor.Setup(x => x.ReencryptLegacySecretsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(0);
            return twoFactor.Object;
        });
        services.AddScoped<IGroupService, GroupService>();

        var mockCryptoProvider = new Mock<Aegis.Common.ICryptoProvider>();
        mockCryptoProvider.Setup(x => x.HashPasswordAsync(It.IsAny<string>()))
            .ReturnsAsync((string password) => $"hash:{password}");
        mockCryptoProvider.Setup(x => x.VerifyPasswordAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string password, string hash) => hash == $"hash:{password}");
        mockCryptoProvider.Setup(x => x.GenerateSessionKeyAsync())
            .ReturnsAsync(new byte[] { 1, 2, 3, 4 });
        mockCryptoProvider.Setup(x => x.HashAsync(It.IsAny<string>()))
            .ReturnsAsync("hashed_session_key");

        services.AddSingleton(mockCryptoProvider.Object);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:TotpEncryptionKey"] = Convert.ToBase64String(Enumerable.Repeat((byte)11, 32).ToArray())
            })
            .Build();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug));

        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task AuthenticationFlow_ShouldAuthenticateRegisteredUser()
    {
        var registrationService = _serviceProvider.GetRequiredService<IUserRegistrationService>();
        var authService = _serviceProvider.GetRequiredService<IUserAuthenticationService>();

        var user = await registrationService.RegisterUserAsync(
            "integration-auth-user",
            "integration-auth@example.com",
            "password123",
            "public_key");

        var authResult = await authService.AuthenticateUserAsync(
            "integration-auth-user",
            "password123",
            "Integration Test",
            "127.0.0.1");

        Assert.NotNull(authResult);
        Assert.Equal(user.Id, authResult.Value.Session.UserId);
    }

    [Fact]
    public async Task MessageSendReceive_WithAcks_ShouldAcknowledgeAndPushToRecipient()
    {
        var antiSpam = new Mock<IAntiSpamClient>();
        antiSpam.Setup(x => x.CheckMessageAsync(It.IsAny<ulong>(), It.IsAny<byte[]>())).ReturnsAsync(true);

        var messageService = new Mock<IMessageService>();
        messageService
            .Setup(x => x.SendPrivateMessageAsync(It.IsAny<ulong>(), It.IsAny<ulong>(), It.IsAny<string>(), It.IsAny<MessageContentType>()))
            .ReturnsAsync(new DataMessage
            {
                Id = 501,
                FromUserId = 10,
                ToUserId = 20,
                Content = "hello",
                CreatedAt = DateTime.UtcNow
            });

        var sender = new TestMessageSender();
        var sessionManager = new SessionManager(new AegisCryptoProvider(), new Aegis.Common.Logging.NullLogger());
        var handler = new MessageHandler(antiSpam.Object, messageService.Object, sender, sessionManager, new Aegis.Common.Logging.NullLogger());

        using var sourceSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        using var destinationSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var sourceContext = new ConnectionContext(sourceSocket, 1111ul);
        var destinationContext = new ConnectionContext(destinationSocket, 2222ul);

        sessionManager.CreateSession(sourceContext.ConnectionId);
        sessionManager.EstablishHandshake(sourceContext.ConnectionId, new byte[32]);
        sessionManager.AuthenticateSession(sourceContext.ConnectionId, 10, "sender");

        sessionManager.CreateSession(destinationContext.ConnectionId);
        sessionManager.EstablishHandshake(destinationContext.ConnectionId, new byte[32]);
        sessionManager.AuthenticateSession(destinationContext.ConnectionId, 20, "recipient");

        var payload = Encoding.UTF8.GetBytes("{\"recipientId\":20,\"content\":\"hello\"}");
        var message = new ProtocolMessage
        {
            Type = MessageType.Message,
            SequenceId = 99,
            Payload = payload,
            PayloadLength = (uint)payload.Length
        };

        await handler.HandleAsync(sourceContext, message);

        Assert.True(handler.AckSent);
        Assert.Equal(99ul, handler.AckSequenceId);

        var decodedTypes = sender.SentMessages
            .Select(buffer => MessageEncoder.Decode(buffer))
            .Select(x => x.Type)
            .ToList();

        Assert.Contains(MessageType.Ack, decodedTypes);
        messageService.Verify(
            x => x.SendPrivateMessageAsync(10, It.IsAny<ulong>(), It.IsAny<string>(), MessageContentType.Text),
            Times.Once);
    }

    [Fact]
    public async Task GroupCreation_AndMemberAdd_ShouldPersistMembership()
    {
        var registrationService = _serviceProvider.GetRequiredService<IUserRegistrationService>();
        var groupService = _serviceProvider.GetRequiredService<IGroupService>();
        var groupRepository = _serviceProvider.GetRequiredService<IGroupRepository>();

        var owner = await registrationService.RegisterUserAsync("group-owner", "group-owner@example.com", "password123", "pk1");
        var member = await registrationService.RegisterUserAsync("group-member", "group-member@example.com", "password123", "pk2");

        var group = await groupService.CreateGroupAsync(owner.Id, "integration-group", "for integration test");

        await groupRepository.AddMemberAsync(new GroupMember
        {
            GroupId = group.Id,
            UserId = member.Id,
            Role = GroupMemberRole.Member,
            JoinedAt = DateTime.UtcNow,
            IsActive = true,
            CanSendMessages = true
        });

        var members = await groupRepository.GetGroupMembersAsync(group.Id);

        Assert.Equal(2, members.Count(m => m.IsActive));
        Assert.Contains(members, x => x.UserId == owner.Id && x.Role == GroupMemberRole.Owner);
        Assert.Contains(members, x => x.UserId == member.Id);
    }

    [Fact]
    public async Task OfflineMessageDelivery_ShouldPushPendingMessagesForOnlineUser()
    {
        var sender = new TestMessageSender();
        var sessionManager = new SessionManager(new AegisCryptoProvider(), new Aegis.Common.Logging.NullLogger());

        using var recipientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var recipientContext = new ConnectionContext(recipientSocket, 7777ul);
        sessionManager.CreateSession(recipientContext.ConnectionId);
        sessionManager.EstablishHandshake(recipientContext.ConnectionId, new byte[32]);
        sessionManager.AuthenticateSession(recipientContext.ConnectionId, 55, "offline-target");

        var messageRepository = new Mock<IMessageRepository>();
        messageRepository
            .Setup(x => x.TrimUndeliveredMessagesAsync(55, It.IsAny<int>()))
            .ReturnsAsync(0);

        messageRepository
            .Setup(x => x.GetUndeliveredMessagesAsync(55))
            .ReturnsAsync(new[]
            {
                new DataMessage
                {
                    Id = 910,
                    FromUserId = 1,
                    ToUserId = 55,
                    Content = "queued",
                    ContentType = MessageContentType.Text,
                    CreatedAt = DateTime.UtcNow,
                    IsDelivered = false,
                    IsRead = false,
                    FromUser = new User { Id = 1, Username = "sender" },
                    ToUser = new User { Id = 55, Username = "offline-target" }
                }
            });

        messageRepository
            .Setup(x => x.MarkMessagesDeliveredAsync(It.IsAny<IEnumerable<ulong>>(), 55))
            .Returns(Task.CompletedTask);

        await DeliverOfflineMessagesOnceAsync(sessionManager, messageRepository.Object, sender, CancellationToken.None);

        messageRepository.Verify(x => x.MarkMessagesDeliveredAsync(It.Is<IEnumerable<ulong>>(ids => ids.Contains(910ul)), 55), Times.Once);

        var eventTypes = sender.SentMessages
            .Select(buffer => MessageEncoder.Decode(buffer))
            .Select(x => x.Type)
            .ToList();

        Assert.Contains(MessageType.PrivateChatMessageEvent, eventTypes);
    }

    [Fact]
    public async Task MediaUploadDownload_ShouldReturnChunksAndComplete()
    {
        using var fileStore = new FileTransferStore();
        var sender = new TestMessageSender();
        var sessionManager = new SessionManager(new AegisCryptoProvider(), new Aegis.Common.Logging.NullLogger());
        var handler = new FileTransferHandler(
            sessionManager,
            sender,
            fileStore,
            new FileDownloadRateLimiter(bytesPerSecond: 16 * 1024 * 1024),
            Mock.Of<ILogger<FileTransferHandler>>());

        using var uploaderSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        using var downloaderSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var uploaderContext = new ConnectionContext(uploaderSocket, 12001ul);
        var downloaderContext = new ConnectionContext(downloaderSocket, 12002ul);

        sessionManager.CreateSession(uploaderContext.ConnectionId);
        sessionManager.EstablishHandshake(uploaderContext.ConnectionId, new byte[32]);
        sessionManager.AuthenticateSession(uploaderContext.ConnectionId, 901, "uploader");

        sessionManager.CreateSession(downloaderContext.ConnectionId);
        sessionManager.EstablishHandshake(downloaderContext.ConnectionId, new byte[32]);
        sessionManager.AuthenticateSession(downloaderContext.ConnectionId, 902, "downloader");

        var content = Encoding.UTF8.GetBytes("integration-file-content");

        await SendFileTransferAsync(handler, uploaderContext, 1, new FileTransferRequest(
            Action: "init",
            FileName: "sample.txt",
            MimeType: "text/plain",
            TotalSize: content.Length,
            TotalChunks: 1,
            AllowedUserIds: new[] { 902ul }));

        var initResponse = PayloadSerializer.Deserialize<FileTransferResponse>(MessageEncoder.Decode(sender.SentMessages[^1]).Payload);
        Assert.NotNull(initResponse);
        Assert.True(initResponse!.Success);
        Assert.False(string.IsNullOrWhiteSpace(initResponse.TransferId));

        await SendFileTransferAsync(handler, uploaderContext, 2, new FileTransferRequest(
            Action: "chunk",
            TransferId: initResponse.TransferId,
            ChunkIndex: 0,
            ChunkDataBase64: Convert.ToBase64String(content)));

        await SendFileTransferAsync(handler, uploaderContext, 3, new FileTransferRequest(
            Action: "complete",
            TransferId: initResponse.TransferId));

        var completeResponse = PayloadSerializer.Deserialize<FileTransferResponse>(MessageEncoder.Decode(sender.SentMessages[^1]).Payload);
        Assert.NotNull(completeResponse);
        Assert.True(completeResponse!.Success);
        Assert.False(string.IsNullOrWhiteSpace(completeResponse.FileId));

        await SendFileTransferAsync(handler, downloaderContext, 4, new FileTransferRequest(
            Action: "download",
            FileId: completeResponse.FileId));

        var sentTypes = sender.SentMessages.Select(buffer => MessageEncoder.Decode(buffer)).Select(x => x.Type).ToList();
        Assert.Contains(MessageType.FileTransferResponse, sentTypes);
        Assert.Contains(MessageType.FileTransferChunk, sentTypes);
    }

    [Fact]
    public void ConnectionTimeoutAndReconnection_ShouldDropExpiredFrameAndRestoreSession()
    {
        using var firstSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        using var secondSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        var firstContext = new ConnectionContext(firstSocket, 45001ul);
        var secondContext = new ConnectionContext(secondSocket, 45002ul);

        firstContext.AppendIncomingData(new byte[] { 0xAA, 0xBB, 0xCC });
        var dropped = firstContext.TryDropExpiredIncompleteFrame(TimeSpan.Zero, out var droppedBytes);

        Assert.True(dropped);
        Assert.Equal(3, droppedBytes);

        var sessionManager = new SessionManager(new AegisCryptoProvider(), new Aegis.Common.Logging.NullLogger());
        sessionManager.CreateSession(firstContext.ConnectionId);
        sessionManager.EstablishHandshake(firstContext.ConnectionId, new byte[32]);
        sessionManager.AuthenticateSession(firstContext.ConnectionId, 500, "reconnect-user");

        sessionManager.RemoveSession(firstContext.ConnectionId);

        sessionManager.CreateSession(secondContext.ConnectionId);
        sessionManager.EstablishHandshake(secondContext.ConnectionId, new byte[32]);
        var authenticated = sessionManager.AuthenticateSession(secondContext.ConnectionId, 500, "reconnect-user");

        Assert.True(authenticated);
        Assert.True(sessionManager.TryGetConnectionIdByUserId(500, out var newConnectionId));
        Assert.Equal(secondContext.ConnectionId, newConnectionId);
    }

    [Fact]
    public void RateLimiting_ShouldTriggerWhenLimitExceeded()
    {
        var limiter = new RateLimiter(new RateLimitOptions
        {
            MaxMessagesPerSecond = 1,
            MaxAuthAttemptsPerMinute = 1,
            MaxConnectionsPerIP = 1
        });

        Assert.True(limiter.CanConnect("127.0.0.1"));
        Assert.False(limiter.CanConnect("127.0.0.1"));

        Assert.True(limiter.CanSendAuthRequest(6001ul));
        Assert.False(limiter.CanSendAuthRequest(6001ul));

        Assert.True(limiter.CanSendMessage(6001ul));
        Assert.False(limiter.CanSendMessage(6001ul));
    }

    private static async Task DeliverOfflineMessagesOnceAsync(
        SessionManager sessionManager,
        IMessageRepository messageRepository,
        IMessageSender sender,
        CancellationToken cancellationToken)
    {
        foreach (var userId in sessionManager.GetOnlineUserIds())
        {
            if (!sessionManager.TryGetConnectionIdByUserId(userId, out var connectionId))
                continue;

            await messageRepository.TrimUndeliveredMessagesAsync(userId, 1000);
            var pending = (await messageRepository.GetUndeliveredMessagesAsync(userId))
                .OrderBy(m => m.CreatedAt).Take(1000).ToList();

            if (pending.Count == 0) continue;

            var deliveredIds = new List<ulong>(pending.Count);
            foreach (var msg in pending)
            {
                var payload = PayloadSerializer.Serialize(new PrivateChatMessageEventPayload(
                    msg.Id, msg.FromUserId, msg.ToUserId, msg.Content, msg.ContentType,
                    msg.CreatedAt, Array.Empty<ulong>(), Array.Empty<ulong>(),
                    msg.FromUser?.Username, msg.ToUser?.Username));
                await sender.SendProtocolMessageAsync(connectionId, (ushort)MessageType.PrivateChatMessageEvent, 0, payload, allowUnsigned: false);
                deliveredIds.Add(msg.Id);
            }

            if (deliveredIds.Count > 0)
                await messageRepository.MarkMessagesDeliveredAsync(deliveredIds, userId);
        }
    }

    private static async Task SendFileTransferAsync(FileTransferHandler handler, ConnectionContext context, ulong sequenceId, FileTransferRequest request)
    {
        var payload = PayloadSerializer.Serialize(request);
        var message = new ProtocolMessage
        {
            Type = MessageType.FileTransfer,
            SequenceId = sequenceId,
            Payload = payload,
            PayloadLength = (uint)payload.Length
        };

        await handler.HandleAsync(context, message);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }
}
