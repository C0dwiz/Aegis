using System.Text.Json;
using Aegis.Common;
using Aegis.Data.Entities;
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
    ulong MessageId = 0,
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
    ulong ChannelId = 0,
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
    ulong MessageId = 0,
    string? MessageText = null
);

// ===================== CHANNEL MESSAGE HANDLER =====================

public class ChannelMessageHandler : IMessageHandler
{
    public MessageType Type => MessageType.ChannelMessage;
    
    private readonly IMessageService _messageService;
    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;
    private readonly ILogger<ChannelMessageHandler> _logger;

    public ChannelMessageHandler(
        IMessageService messageService,
        SessionManager sessionManager,
        IMessageSender messageSender,
        ILogger<ChannelMessageHandler> logger)
    {
        _messageService = messageService;
        _sessionManager = sessionManager;
        _messageSender = messageSender;
        _logger = logger;
    }

    public async ValueTask HandleAsync(ConnectionContext context, Aegis.Protocol.Message message)
    {
        try
        {
            var session = _sessionManager.GetAuthenticatedSession(context.ConnectionId);
            if (session == null)
            {
                await SendResponseAsync(context, message.SequenceId, new ChannelMessageResponse(false, MessageText: "Not authenticated"));
                return;
            }

            var payload = JsonSerializer.Deserialize<ChannelMessageRequest>(message.Payload);
            if (payload == null)
            {
                await SendResponseAsync(context, message.SequenceId, new ChannelMessageResponse(false, MessageText: "Invalid payload"));
                return;
            }

            var channelMsg = await _messageService.SendChannelMessageAsync(
                payload.ChannelId, session.UserId, payload.Content,
                payload.ContentType, payload.ReplyToMessageId);

            await SendResponseAsync(context, message.SequenceId, 
                new ChannelMessageResponse(true, channelMsg.Id, "Message sent"));

            _logger.LogInformation("Channel message sent to channel {ChannelId} by user {UserId}", 
                payload.ChannelId, session.UserId);
        }
        catch (UnauthorizedAccessException ex)
        {
            await SendResponseAsync(context, message.SequenceId, new ChannelMessageResponse(false, MessageText: ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            await SendResponseAsync(context, message.SequenceId, new ChannelMessageResponse(false, MessageText: ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending channel message");
            await SendResponseAsync(context, message.SequenceId, new ChannelMessageResponse(false, MessageText: "Internal server error"));
        }
    }

    private async Task SendResponseAsync(ConnectionContext context, ulong sequenceId, ChannelMessageResponse response)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(response);
        var msg = new Aegis.Protocol.Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = ProtocolConstants.VersionMajor,
            VersionMinor = ProtocolConstants.VersionMinor,
            Type = MessageType.Ack,
            SequenceId = sequenceId,
            PayloadLength = (uint)payload.Length,
            Payload = payload,
            Mac = new byte[ProtocolConstants.MacSize]
        };
        var buffer = new byte[ProtocolConstants.HeaderSize + payload.Length + ProtocolConstants.MacSize];
        MessageEncoder.Encode(msg, buffer);
        await _messageSender.SendMessageAsync(context.ConnectionId, buffer);
    }
}

// ===================== CHANNEL CREATE HANDLER =====================

public class ChannelCreateHandler : IMessageHandler
{
    public MessageType Type => MessageType.ChannelCreate;
    
    private readonly IChannelService _channelService;
    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;
    private readonly ILogger<ChannelCreateHandler> _logger;

    public ChannelCreateHandler(
        IChannelService channelService,
        SessionManager sessionManager,
        IMessageSender messageSender,
        ILogger<ChannelCreateHandler> logger)
    {
        _channelService = channelService;
        _sessionManager = sessionManager;
        _messageSender = messageSender;
        _logger = logger;
    }

    public async ValueTask HandleAsync(ConnectionContext context, Aegis.Protocol.Message message)
    {
        try
        {
            var session = _sessionManager.GetAuthenticatedSession(context.ConnectionId);
            if (session == null)
            {
                await SendResponseAsync(context, message.SequenceId, new ChannelCreateResponse(false, Message: "Not authenticated"));
                return;
            }

            var payload = JsonSerializer.Deserialize<ChannelCreateRequest>(message.Payload);
            if (payload == null)
            {
                await SendResponseAsync(context, message.SequenceId, new ChannelCreateResponse(false, Message: "Invalid payload"));
                return;
            }

            var channel = await _channelService.CreateChannelAsync(
                session.UserId, payload.Name, payload.Description, payload.Type);

            await SendResponseAsync(context, message.SequenceId, 
                new ChannelCreateResponse(true, channel.Id, "Channel created"));

            _logger.LogInformation("Channel '{ChannelName}' created by user {UserId}", payload.Name, session.UserId);
        }
        catch (ArgumentException ex)
        {
            await SendResponseAsync(context, message.SequenceId, new ChannelCreateResponse(false, Message: ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating channel");
            await SendResponseAsync(context, message.SequenceId, new ChannelCreateResponse(false, Message: "Internal server error"));
        }
    }

    private async Task SendResponseAsync(ConnectionContext context, ulong sequenceId, ChannelCreateResponse response)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(response);
        var msg = new Aegis.Protocol.Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = ProtocolConstants.VersionMajor,
            VersionMinor = ProtocolConstants.VersionMinor,
            Type = MessageType.Ack,
            SequenceId = sequenceId,
            PayloadLength = (uint)payload.Length,
            Payload = payload,
            Mac = new byte[ProtocolConstants.MacSize]
        };
        var buffer = new byte[ProtocolConstants.HeaderSize + payload.Length + ProtocolConstants.MacSize];
        MessageEncoder.Encode(msg, buffer);
        await _messageSender.SendMessageAsync(context.ConnectionId, buffer);
    }
}

// ===================== PRIVATE CHAT MESSAGE HANDLER =====================

public class PrivateChatMessageHandler : IMessageHandler
{
    public MessageType Type => MessageType.PrivateChatMessage;
    
    private readonly IMessageService _messageService;
    private readonly IUserSearchService _userSearchService;
    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;
    private readonly ILogger<PrivateChatMessageHandler> _logger;

    public PrivateChatMessageHandler(
        IMessageService messageService,
        IUserSearchService userSearchService,
        SessionManager sessionManager,
        IMessageSender messageSender,
        ILogger<PrivateChatMessageHandler> logger)
    {
        _messageService = messageService;
        _userSearchService = userSearchService;
        _sessionManager = sessionManager;
        _messageSender = messageSender;
        _logger = logger;
    }

    public async ValueTask HandleAsync(ConnectionContext context, Aegis.Protocol.Message message)
    {
        try
        {
            var session = _sessionManager.GetAuthenticatedSession(context.ConnectionId);
            if (session == null)
            {
                await SendResponseAsync(context, message.SequenceId, new PrivateChatMessageResponse(false, MessageText: "Not authenticated"));
                return;
            }

            var payload = JsonSerializer.Deserialize<PrivateChatMessageRequest>(message.Payload);
            if (payload == null)
            {
                await SendResponseAsync(context, message.SequenceId, new PrivateChatMessageResponse(false, MessageText: "Invalid payload"));
                return;
            }

            // Check target user exists
            var targetUser = await _userSearchService.FindUserByIdAsync(payload.ToUserId);
            if (targetUser == null)
            {
                await SendResponseAsync(context, message.SequenceId, new PrivateChatMessageResponse(false, MessageText: "User not found"));
                return;
            }

            var privateMsg = await _messageService.SendPrivateMessageAsync(
                session.UserId, payload.ToUserId, payload.Content, payload.ContentType);

            await SendResponseAsync(context, message.SequenceId, 
                new PrivateChatMessageResponse(true, privateMsg.Id, "Message sent"));

            _logger.LogInformation("Private message sent from user {FromUserId} to user {ToUserId}", 
                session.UserId, payload.ToUserId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending private message");
            await SendResponseAsync(context, message.SequenceId, new PrivateChatMessageResponse(false, MessageText: "Internal server error"));
        }
    }

    private async Task SendResponseAsync(ConnectionContext context, ulong sequenceId, PrivateChatMessageResponse response)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(response);
        var msg = new Aegis.Protocol.Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = ProtocolConstants.VersionMajor,
            VersionMinor = ProtocolConstants.VersionMinor,
            Type = MessageType.Ack,
            SequenceId = sequenceId,
            PayloadLength = (uint)payload.Length,
            Payload = payload,
            Mac = new byte[ProtocolConstants.MacSize]
        };
        var buffer = new byte[ProtocolConstants.HeaderSize + payload.Length + ProtocolConstants.MacSize];
        MessageEncoder.Encode(msg, buffer);
        await _messageSender.SendMessageAsync(context.ConnectionId, buffer);
    }
}


