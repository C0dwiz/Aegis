using Aegis.Common;
using Aegis.Data.Entities;
using Aegis.Data.Repositories;
using Aegis.Data.Services;
using Aegis.Protocol;
using Aegis.Transport;
using Microsoft.Extensions.Logging;

namespace Aegis.Handlers;

public record ChatListRequest();

public record ChatListResponse(
    bool Success,
    IReadOnlyList<ChatListItem> Chats,
    string? Message = null
);

public record ChatListItem(
    ulong ChatId,
    string Type,
    string Title,
    string? AvatarUrl,
    string? PresenceStatus = null,
    string? LastMessage = null,
    DateTime? LastMessageAt = null,
    int UnreadCount = 0,
    ulong? PeerUserId = null,
    ulong? ChannelId = null
);

public record PrivateChatHistoryRequest(
    ulong PeerUserId,
    int Limit = 50,
    ulong? BeforeMessageId = null
);

public record PrivateChatHistoryResponse(
    bool Success,
    ulong PeerUserId,
    IReadOnlyList<PrivateChatHistoryItem> Messages,
    string? Message = null
);

public record PrivateChatHistoryItem(
    ulong Id,
    ulong FromUserId,
    ulong ToUserId,
    string Content,
    MessageContentType ContentType,
    DateTime CreatedAt,
    IReadOnlyList<ulong> DeliveredTo,
    IReadOnlyList<ulong> ReadBy,
    string? FromUsername = null,
    string? Username = null
);

public record ChannelHistoryRequest(
    ulong ChannelId,
    int Limit = 50,
    ulong? BeforeMessageId = null
);

public record ChannelHistoryResponse(
    bool Success,
    ulong ChannelId,
    string? ChannelName,
    IReadOnlyList<ChannelHistoryItem> Messages,
    string? Message = null
);

public record ChannelHistoryItem(
    ulong Id,
    ulong ChannelId,
    ulong FromUserId,
    string Content,
    MessageContentType ContentType,
    DateTime CreatedAt,
    IReadOnlyList<ulong> DeliveredTo,
    IReadOnlyList<ulong> ReadBy,
    string? FromUsername = null,
    string? ChannelName = null
);

public record PrivateChatMessageEventPayload(
    ulong Id,
    ulong FromUserId,
    ulong ToUserId,
    string Content,
    MessageContentType ContentType,
    DateTime CreatedAt,
    IReadOnlyList<ulong> DeliveredTo,
    IReadOnlyList<ulong> ReadBy,
    string? FromUsername = null,
    string? Username = null
);

public record ChannelMessageEventPayload(
    ulong Id,
    ulong ChannelId,
    ulong FromUserId,
    string Content,
    MessageContentType ContentType,
    DateTime CreatedAt,
    IReadOnlyList<ulong> DeliveredTo,
    IReadOnlyList<ulong> ReadBy,
    string? FromUsername = null,
    string? ChannelName = null
);

public class ChatListHandler : IMessageHandler
{
    public MessageType Type => MessageType.ChatListRequest;

    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;
    private readonly IPrivateChatRepository _privateChatRepository;
    private readonly IChannelRepository _channelRepository;
    private readonly IMessageRepository _messageRepository;
    private readonly ILogger<ChatListHandler> _logger;
    private readonly UserPresenceResolver _presenceResolver;

    public ChatListHandler(
        SessionManager sessionManager,
        IMessageSender messageSender,
        IPrivateChatRepository privateChatRepository,
        IChannelRepository channelRepository,
        IMessageRepository messageRepository,
        ILogger<ChatListHandler> logger,
        UserPresenceResolver presenceResolver)
    {
        _sessionManager = sessionManager;
        _messageSender = messageSender;
        _privateChatRepository = privateChatRepository;
        _channelRepository = channelRepository;
        _messageRepository = messageRepository;
        _logger = logger;
        _presenceResolver = presenceResolver;
    }

    public async ValueTask HandleAsync(ConnectionContext context, Aegis.Protocol.Message message)
    {
        try
        {
            var session = _sessionManager.GetAuthenticatedSession(context.ConnectionId);
            if (session == null)
            {
                await SendResponseAsync(context, message.SequenceId, new ChatListResponse(false, Array.Empty<ChatListItem>(), "Not authenticated"));
                return;
            }

            var unreadBySender = await _messageRepository.GetUnreadCountsBySenderAsync(session.UserId);
            var chatItems = new List<ChatListItem>();

            var privateChats = await _privateChatRepository.GetUserPrivateChatsAsync(session.UserId);
            foreach (var chat in privateChats)
            {
                var peer = chat.User1Id == session.UserId ? chat.User2 : chat.User1;
                var peerId = chat.User1Id == session.UserId ? chat.User2Id : chat.User1Id;

                chatItems.Add(new ChatListItem(
                    ChatId: chat.Id,
                    Type: "direct",
                    Title: peer?.Username ?? $"user-{peerId}",
                    AvatarUrl: peer?.AvatarUrl,
                    PresenceStatus: _presenceResolver.Resolve(peerId, peer?.LastSeenAt),
                    LastMessage: chat.LastMessage?.Content,
                    LastMessageAt: chat.LastMessage?.CreatedAt ?? chat.LastActivityAt,
                    UnreadCount: unreadBySender.TryGetValue(peerId, out var unread) ? unread : 0,
                    PeerUserId: peerId,
                    ChannelId: null));
            }

                var channelSummaries = await _channelRepository.GetUserChannelChatSummariesAsync(session.UserId);
                foreach (var summary in channelSummaries)
            {
                chatItems.Add(new ChatListItem(
                    ChatId: summary.ChannelId,
                    Type: summary.Type == ChannelType.Group ? "group" : "channel",
                    Title: summary.Name,
                    AvatarUrl: summary.AvatarUrl,
                    PresenceStatus: null,
                    LastMessage: summary.LastMessage,
                    LastMessageAt: summary.LastMessageAt,
                    UnreadCount: summary.UnreadCount,
                    PeerUserId: null,
                    ChannelId: summary.ChannelId));
            }

            var ordered = chatItems
                .OrderByDescending(x => x.LastMessageAt ?? DateTime.MinValue)
                .ToList();

            await SendResponseAsync(context, message.SequenceId, new ChatListResponse(true, ordered));
        }
        catch (Exception ex)
        {
            _logger.LogHandlerError(ex, "chat_list_load", context, message, _sessionManager);
            await SendResponseAsync(context, message.SequenceId, new ChatListResponse(false, Array.Empty<ChatListItem>(), "Internal server error"));
        }
    }

    private async Task SendResponseAsync(ConnectionContext context, ulong sequenceId, ChatListResponse response)
    {
        var payload = PayloadSerializer.Serialize(response);
        await _messageSender.SendProtocolMessageAsync(
            context.ConnectionId,
            (ushort)MessageType.ChatListResponse,
            sequenceId,
            payload);
    }
}

public class PrivateChatHistoryHandler : IMessageHandler
{
    public MessageType Type => MessageType.PrivateChatHistoryRequest;

    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;
    private readonly IMessageRepository _messageRepository;
    private readonly IUserSearchService _userSearchService;
    private readonly ILogger<PrivateChatHistoryHandler> _logger;

    public PrivateChatHistoryHandler(
        SessionManager sessionManager,
        IMessageSender messageSender,
        IMessageRepository messageRepository,
        IUserSearchService userSearchService,
        ILogger<PrivateChatHistoryHandler> logger)
    {
        _sessionManager = sessionManager;
        _messageSender = messageSender;
        _messageRepository = messageRepository;
        _userSearchService = userSearchService;
        _logger = logger;
    }

    public async ValueTask HandleAsync(ConnectionContext context, Aegis.Protocol.Message message)
    {
        try
        {
            var session = _sessionManager.GetAuthenticatedSession(context.ConnectionId);
            if (session == null)
            {
                await SendResponseAsync(context, message.SequenceId,
                    new PrivateChatHistoryResponse(false, 0, Array.Empty<PrivateChatHistoryItem>(), "Not authenticated"));
                return;
            }

            var payload = PayloadSerializer.Deserialize<PrivateChatHistoryRequest>(message.Payload);
            if (payload == null || payload.PeerUserId == 0)
            {
                await SendResponseAsync(context, message.SequenceId,
                    new PrivateChatHistoryResponse(false, 0, Array.Empty<PrivateChatHistoryItem>(), "Invalid payload"));
                return;
            }

            var peer = await _userSearchService.FindUserByIdAsync(payload.PeerUserId);
            if (peer == null)
            {
                await SendResponseAsync(context, message.SequenceId,
                    new PrivateChatHistoryResponse(false, payload.PeerUserId, Array.Empty<PrivateChatHistoryItem>(), "User not found"));
                return;
            }

            var limit = Math.Clamp(payload.Limit, 1, 200);
            var history = await _messageRepository.GetConversationBeforeAsync(
                session.UserId,
                payload.PeerUserId,
                payload.BeforeMessageId,
                limit);

            var ordered = history
                .OrderBy(m => m.CreatedAt)
                .Select(m => new PrivateChatHistoryItem(
                    Id: m.Id,
                    FromUserId: m.FromUserId,
                    ToUserId: m.ToUserId,
                    Content: m.Content,
                    ContentType: m.ContentType,
                    CreatedAt: m.CreatedAt,
                    DeliveredTo: m.IsDelivered ? [m.ToUserId] : [],
                    ReadBy: m.IsRead ? [m.ToUserId] : [],
                    FromUsername: m.FromUserId == session.UserId ? session.Username : peer.Username,
                    Username: m.FromUserId == session.UserId ? session.Username : peer.Username))
                .ToList();

            var readMessageIds = history
                .Where(m => m.ToUserId == session.UserId && !m.IsRead && !m.IsDeleted)
                .Select(m => m.Id)
                .Distinct()
                .ToArray();

            if (readMessageIds.Length > 0)
            {
                await _messageRepository.MarkMessagesReadAsync(readMessageIds, session.UserId);
                await SendReadReceiptEventToPeerAsync(payload.PeerUserId, session.UserId, readMessageIds);
            }

            await SendResponseAsync(context, message.SequenceId,
                new PrivateChatHistoryResponse(true, payload.PeerUserId, ordered));
        }
        catch (Exception ex)
        {
            _logger.LogHandlerError(ex, "private_history_load", context, message, _sessionManager);
            await SendResponseAsync(context, message.SequenceId,
                new PrivateChatHistoryResponse(false, 0, Array.Empty<PrivateChatHistoryItem>(), "Internal server error"));
        }
    }

    private async Task SendResponseAsync(ConnectionContext context, ulong sequenceId, PrivateChatHistoryResponse response)
    {
        var payload = PayloadSerializer.Serialize(response);
        await _messageSender.SendProtocolMessageAsync(
            context.ConnectionId,
            (ushort)MessageType.PrivateChatHistoryResponse,
            sequenceId,
            payload);
    }

    private async Task SendReadReceiptEventToPeerAsync(ulong peerUserId, ulong readerUserId, IReadOnlyList<ulong> messageIds)
    {
        if (!_sessionManager.TryGetConnectionIdByUserId(peerUserId, out var peerConnectionId))
        {
            return;
        }

        var payload = PayloadSerializer.Serialize(new MessageStatusEventPayload(
            Success: true,
            MessageIds: messageIds.ToArray(),
            DeliveredTo: null,
            ReadBy: readerUserId,
            ProcessedAt: DateTime.UtcNow));

        await _messageSender.SendProtocolMessageAsync(
            peerConnectionId,
            (ushort)MessageType.MessageStatusEvent,
            0,
            payload);
    }
}

internal sealed record MessageStatusEventPayload(
    bool Success,
    ulong[] MessageIds,
    ulong? DeliveredTo,
    ulong? ReadBy,
    DateTime ProcessedAt);

public class ChannelHistoryHandler : IMessageHandler
{
    public MessageType Type => MessageType.ChannelHistoryRequest;

    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;
    private readonly IChannelRepository _channelRepository;
    private readonly ILogger<ChannelHistoryHandler> _logger;

    public ChannelHistoryHandler(
        SessionManager sessionManager,
        IMessageSender messageSender,
        IChannelRepository channelRepository,
        ILogger<ChannelHistoryHandler> logger)
    {
        _sessionManager = sessionManager;
        _messageSender = messageSender;
        _channelRepository = channelRepository;
        _logger = logger;
    }

    public async ValueTask HandleAsync(ConnectionContext context, Aegis.Protocol.Message message)
    {
        try
        {
            var session = _sessionManager.GetAuthenticatedSession(context.ConnectionId);
            if (session == null)
            {
                await SendResponseAsync(context, message.SequenceId,
                    new ChannelHistoryResponse(false, 0, null, Array.Empty<ChannelHistoryItem>(), "Not authenticated"));
                return;
            }

            var payload = PayloadSerializer.Deserialize<ChannelHistoryRequest>(message.Payload);
            if (payload == null || payload.ChannelId == 0)
            {
                await SendResponseAsync(context, message.SequenceId,
                    new ChannelHistoryResponse(false, 0, null, Array.Empty<ChannelHistoryItem>(), "Invalid payload"));
                return;
            }

            var member = await _channelRepository.GetChannelMemberAsync(payload.ChannelId, session.UserId);
            if (member == null)
            {
                await SendResponseAsync(context, message.SequenceId,
                    new ChannelHistoryResponse(false, payload.ChannelId, null, Array.Empty<ChannelHistoryItem>(), "Not a channel member"));
                return;
            }

            var channel = await _channelRepository.GetByIdAsync(payload.ChannelId);
            var limit = Math.Clamp(payload.Limit, 1, 200);

            var history = await _channelRepository.GetChannelMessagesBeforeAsync(
                payload.ChannelId,
                payload.BeforeMessageId,
                limit);

            var ordered = history
                .OrderBy(m => m.CreatedAt)
                .Select(m => new ChannelHistoryItem(
                    Id: m.Id,
                    ChannelId: m.ChannelId,
                    FromUserId: m.FromUserId,
                    Content: m.Content,
                    ContentType: m.ContentType,
                    CreatedAt: m.CreatedAt,
                    DeliveredTo: m.IsDelivered ? [session.UserId] : [],
                    ReadBy: m.IsRead ? [session.UserId] : [],
                    FromUsername: m.FromUser?.Username,
                    ChannelName: channel?.Name))
                .ToList();

            await SendResponseAsync(context, message.SequenceId,
                new ChannelHistoryResponse(true, payload.ChannelId, channel?.Name, ordered));
        }
        catch (Exception ex)
        {
            _logger.LogHandlerError(ex, "channel_history_load", context, message, _sessionManager);
            await SendResponseAsync(context, message.SequenceId,
                new ChannelHistoryResponse(false, 0, null, Array.Empty<ChannelHistoryItem>(), "Internal server error"));
        }
    }

    private async Task SendResponseAsync(ConnectionContext context, ulong sequenceId, ChannelHistoryResponse response)
    {
        var payload = PayloadSerializer.Serialize(response);
        await _messageSender.SendProtocolMessageAsync(
            context.ConnectionId,
            (ushort)MessageType.ChannelHistoryResponse,
            sequenceId,
            payload);
    }
}
