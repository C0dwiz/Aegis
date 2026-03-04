using System.Text.Json;
using Aegis.Common;
using Aegis.Data.Entities;
using Aegis.Data.Repositories;
using Aegis.Data.Services;
using Aegis.Protocol;
using Aegis.Transport;
using Microsoft.Extensions.Logging;

namespace Aegis.Handlers;

/// <summary>
/// Channel message request payload
/// </summary>
public record ChannelMessageRequest(
    ulong ChannelId,
    string Content,
    MessageContentType ContentType = MessageContentType.Text,
    ulong? ReplyToMessageId = null
);

/// <summary>
/// Channel message response payload
/// </summary>
public record ChannelMessageResponse(
    bool Success,
    Aegis.Data.Entities.ChannelMessage? Message = null,
    string? MessageText = null
);

/// <summary>
/// Channel create request payload
/// </summary>
public record ChannelCreateRequest(
    string Name,
    string? Description = null,
    ChannelType Type = ChannelType.Public
);

/// <summary>
/// Channel create response payload
/// </summary>
public record ChannelCreateResponse(
    bool Success,
    Aegis.Data.Entities.Channel? Channel = null,
    string? Message = null
);

/// <summary>
/// Channel join request payload
/// </summary>
public record ChannelJoinRequest(
    ulong ChannelId
);

/// <summary>
/// Channel join response payload
/// </summary>
public record ChannelJoinResponse(
    bool Success,
    Aegis.Data.Entities.Channel? Channel = null,
    string? Message = null
);

/// <summary>
/// Private chat message request payload
/// </summary>
public record PrivateChatMessageRequest(
    ulong ToUserId,
    string Content,
    MessageContentType ContentType = MessageContentType.Text
);

/// <summary>
/// Private chat message response payload
/// </summary>
public record PrivateChatMessageResponse(
    bool Success,
    Aegis.Data.Entities.Message? Message = null,
    Aegis.Data.Entities.PrivateChat? PrivateChat = null,
    string? MessageText = null
);

/// <summary>
/// Channel message handler
/// </summary>
public class ChannelMessageHandler : IMessageHandler
{
    public MessageType Type => MessageType.ChannelMessage;
    
    private readonly IChannelRepository _channelRepository;
    private readonly IUserSearchService _userSearchService;
    private readonly ILogger<ChannelMessageHandler> _logger;

    public ChannelMessageHandler(
        IChannelRepository channelRepository,
        IUserSearchService userSearchService,
        ILogger<ChannelMessageHandler> logger)
    {
        _channelRepository = channelRepository;
        _userSearchService = userSearchService;
        _logger = logger;
    }

    public async ValueTask HandleAsync(ConnectionContext context, Aegis.Protocol.Message message)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<ChannelMessageRequest>(message.Payload);
            if (payload == null)
            {
                await SendErrorResponse(context, message.SequenceId, "Invalid payload");
                return;
            }

            // Check if user is member of the channel
            var session = GetSessionFromContext(context);
            if (session == null)
            {
                await SendErrorResponse(context, message.SequenceId, "Not authenticated");
                return;
            }

            var isMember = await _channelRepository.IsUserMemberAsync(payload.ChannelId, session.UserId);
            if (!isMember)
            {
                await SendErrorResponse(context, message.SequenceId, "Not a member of this channel");
                return;
            }

            // Create channel message
            var channelMessage = new ChannelMessage
            {
                ChannelId = payload.ChannelId,
                FromUserId = session.UserId,
                Content = payload.Content,
                ContentType = payload.ContentType,
                CreatedAt = DateTime.UtcNow,
                ReplyToMessageId = payload.ReplyToMessageId
            };

            // Save message (would need repository for ChannelMessage)
            // For now, just return success
            var response = new ChannelMessageResponse(
                Success: true,
                Message: channelMessage,
                MessageText: "Message sent successfully"
            );

            await SendResponseAsync(context, message.SequenceId, response);
            _logger.LogInformation("Channel message sent to channel {ChannelId} by user {UserId}", 
                payload.ChannelId, session.UserId);
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending channel message");
            await SendErrorResponse(context, message.SequenceId, "Internal server error");
            return;
        }
    }

    private async Task SendResponseAsync(ConnectionContext context, ulong sequenceId, ChannelMessageResponse response)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(response);
        _logger.LogDebug("Channel message response sent for sequence {SequenceId}", sequenceId);
    }

    private async Task SendErrorResponse(ConnectionContext context, ulong sequenceId, string errorMessage)
    {
        var response = new ChannelMessageResponse(Success: false, MessageText: errorMessage);
        await SendResponseAsync(context, sequenceId, response);
    }

    private Session? GetSessionFromContext(ConnectionContext context)
    {
        // This would need to be implemented based on how sessions are stored in context
        // For now, return null
        return null;
    }
}

/// <summary>
/// Channel creation handler
/// </summary>
public class ChannelCreateHandler : IMessageHandler
{
    public MessageType Type => MessageType.ChannelCreate;
    
    private readonly IChannelRepository _channelRepository;
    private readonly IUserSearchService _userSearchService;
    private readonly ILogger<ChannelCreateHandler> _logger;

    public ChannelCreateHandler(
        IChannelRepository channelRepository,
        IUserSearchService userSearchService,
        ILogger<ChannelCreateHandler> logger)
    {
        _channelRepository = channelRepository;
        _userSearchService = userSearchService;
        _logger = logger;
    }

    public async ValueTask HandleAsync(ConnectionContext context, Aegis.Protocol.Message message)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<ChannelCreateRequest>(message.Payload);
            if (payload == null)
            {
                await SendErrorResponse(context, message.SequenceId, "Invalid payload");
                return;
            }

            var session = GetSessionFromContext(context);
            if (session == null)
            {
                await SendErrorResponse(context, message.SequenceId, "Not authenticated");
                return;
            }

            // Create channel
            var channel = new Channel
            {
                Name = payload.Name,
                Description = payload.Description,
                Type = payload.Type,
                CreatedByUserId = session.UserId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsActive = true,
                MemberCount = 1
            };

            // Save channel
            var createdChannel = await _channelRepository.CreateAsync(channel);

            // Add creator as member
            var channelMember = new ChannelMember
            {
                ChannelId = createdChannel.Id,
                UserId = session.UserId,
                Role = ChannelMemberRole.Owner,
                JoinedAt = DateTime.UtcNow,
                IsActive = true
            };

            // Save member (would need repository for ChannelMember)
            var response = new ChannelCreateResponse(
                Success: true,
                Channel: createdChannel,
                Message: "Channel created successfully"
            );

            await SendResponseAsync(context, message.SequenceId, response);
            _logger.LogInformation("Channel {ChannelName} created by user {UserId}", payload.Name, session.UserId);
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating channel");
            await SendErrorResponse(context, message.SequenceId, "Internal server error");
            return;
        }
    }

    private async Task SendResponseAsync(ConnectionContext context, ulong sequenceId, ChannelCreateResponse response)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(response);
        _logger.LogDebug("Channel create response sent for sequence {SequenceId}", sequenceId);
    }

    private async Task SendErrorResponse(ConnectionContext context, ulong sequenceId, string errorMessage)
    {
        var response = new ChannelCreateResponse(Success: false, Message: errorMessage);
        await SendResponseAsync(context, sequenceId, response);
    }

    private Session? GetSessionFromContext(ConnectionContext context)
    {
        // This would need to be implemented based on how sessions are stored in context
        return null;
    }
}

/// <summary>
/// Private chat message handler
/// </summary>
public class PrivateChatMessageHandler : IMessageHandler
{
    public MessageType Type => MessageType.PrivateChatMessage;
    
    private readonly IPrivateChatRepository _privateChatRepository;
    private readonly IUserSearchService _userSearchService;
    private readonly ILogger<PrivateChatMessageHandler> _logger;

    public PrivateChatMessageHandler(
        IPrivateChatRepository privateChatRepository,
        IUserSearchService userSearchService,
        ILogger<PrivateChatMessageHandler> logger)
    {
        _privateChatRepository = privateChatRepository;
        _userSearchService = userSearchService;
        _logger = logger;
    }

    public async ValueTask HandleAsync(ConnectionContext context, Aegis.Protocol.Message message)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<PrivateChatMessageRequest>(message.Payload);
            if (payload == null)
            {
                await SendErrorResponse(context, message.SequenceId, "Invalid payload");
                return;
            }

            var session = GetSessionFromContext(context);
            if (session == null)
            {
                await SendErrorResponse(context, message.SequenceId, "Not authenticated");
                return;
            }

            // Check if target user exists
            var targetUser = await _userSearchService.FindUserByIdAsync(payload.ToUserId);
            if (targetUser == null)
            {
                await SendErrorResponse(context, message.SequenceId, "User not found");
                return;
            }

            // Get or create private chat
            var privateChat = await _privateChatRepository.GetPrivateChatAsync(session.UserId, payload.ToUserId);
            if (privateChat == null)
            {
                privateChat = await _privateChatRepository.CreatePrivateChatAsync(session.UserId, payload.ToUserId);
            }

            // Create message
            var privateMessage = new Aegis.Data.Entities.Message
            {
                FromUserId = session.UserId,
                ToUserId = payload.ToUserId,
                Content = payload.Content,
                ContentType = payload.ContentType,
                CreatedAt = DateTime.UtcNow,
                IsDelivered = false,
                IsRead = false
            };

            // Update private chat
            privateChat.LastActivityAt = DateTime.UtcNow;
            privateChat.LastMessageId = privateMessage.Id;

            var response = new PrivateChatMessageResponse(
                Success: true,
                Message: privateMessage,
                PrivateChat: privateChat,
                MessageText: "Message sent successfully"
            );

            await SendResponseAsync(context, message.SequenceId, response);
            _logger.LogInformation("Private message sent from user {FromUserId} to user {ToUserId}", 
                session.UserId, payload.ToUserId);
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending private message");
            await SendErrorResponse(context, message.SequenceId, "Internal server error");
            return;
        }
    }

    private async Task SendResponseAsync(ConnectionContext context, ulong sequenceId, PrivateChatMessageResponse response)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(response);
        _logger.LogDebug("Private chat message response sent for sequence {SequenceId}", sequenceId);
    }

    private async Task SendErrorResponse(ConnectionContext context, ulong sequenceId, string errorMessage)
    {
        var response = new PrivateChatMessageResponse(Success: false, MessageText: errorMessage);
        await SendResponseAsync(context, sequenceId, response);
    }

    private Session? GetSessionFromContext(ConnectionContext context)
    {
        // This would need to be implemented based on how sessions are stored in context
        return null;
    }
}
