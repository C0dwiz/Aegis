using Aegis.Common;
using Aegis.Data.Services;
using Aegis.Protocol;
using Aegis.Transport;
using Microsoft.Extensions.Logging;

namespace Aegis.Handlers;

// ===================== PAYLOADS =====================

public record MessageEditRequest(
    ulong MessageId,
    string NewContent,
    string Scope = "private", // "private", "channel", "group"
    ulong? ChannelId = null,
    ulong? GroupId = null
);

public record MessageEditResponse(
    bool Success,
    string? Message = null,
    ulong MessageId = 0
);

public record MessageDeleteRequest(
    ulong MessageId,
    string Scope = "private", // "private", "channel", "group"
    ulong? ChannelId = null,
    ulong? GroupId = null
);

public record MessageDeleteResponse(
    bool Success,
    string? Message = null,
    ulong MessageId = 0
);

// ===================== MESSAGE EDIT HANDLER =====================

public class MessageEditHandler : IMessageHandler
{
    public MessageType Type => MessageType.MessageEdit;

    private readonly IMessageService _messageService;
    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;
    private readonly ILogger<MessageEditHandler> _logger;

    public MessageEditHandler(
        IMessageService messageService,
        SessionManager sessionManager,
        IMessageSender messageSender,
        ILogger<MessageEditHandler> logger)
    {
        _messageService = messageService;
        _sessionManager = sessionManager;
        _messageSender = messageSender;
        _logger = logger;
    }

    public async ValueTask HandleAsync(ConnectionContext context, Message message)
    {
        try
        {
            var session = _sessionManager.GetAuthenticatedSession(context.ConnectionId);
            if (session == null)
            {
                await SendResponseAsync(context, message.SequenceId, new MessageEditResponse(false, "Not authenticated"));
                return;
            }

            var request = PayloadSerializer.Deserialize<MessageEditRequest>(message.Payload);
            if (request == null)
            {
                await SendResponseAsync(context, message.SequenceId, new MessageEditResponse(false, "Invalid payload"));
                return;
            }

            switch (request.Scope)
            {
                case "private":
                    var edited = await _messageService.EditMessageAsync(request.MessageId, session.UserId, request.NewContent);
                    await SendResponseAsync(context, message.SequenceId, 
                        new MessageEditResponse(true, "Message edited", edited.Id));
                    break;

                case "channel" when request.ChannelId.HasValue:
                    var chEdited = await _messageService.EditChannelMessageAsync(
                        request.MessageId, session.UserId, request.ChannelId.Value, request.NewContent);
                    await SendResponseAsync(context, message.SequenceId, 
                        new MessageEditResponse(true, "Channel message edited", chEdited.Id));
                    break;

                case "group" when request.GroupId.HasValue:
                    var grEdited = await _messageService.EditGroupMessageAsync(
                        request.MessageId, session.UserId, request.GroupId.Value, request.NewContent);
                    await SendResponseAsync(context, message.SequenceId, 
                        new MessageEditResponse(true, "Group message edited", grEdited.Id));
                    break;

                default:
                    await SendResponseAsync(context, message.SequenceId, 
                        new MessageEditResponse(false, "Invalid scope or missing ID"));
                    break;
            }

            _logger.LogInformation("Message {MessageId} edited by user {UserId} in scope {Scope}", 
                request.MessageId, session.UserId, request.Scope);
        }
        catch (UnauthorizedAccessException ex)
        {
            await SendResponseAsync(context, message.SequenceId, new MessageEditResponse(false, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            await SendResponseAsync(context, message.SequenceId, new MessageEditResponse(false, ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogHandlerError(ex, "message_edit", context, message, _sessionManager);
            await SendResponseAsync(context, message.SequenceId, new MessageEditResponse(false, "Internal server error"));
        }
    }

    private async Task SendResponseAsync(ConnectionContext context, ulong sequenceId, MessageEditResponse response)
    {
        var payload = PayloadSerializer.Serialize(response);
        var msg = new Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = ProtocolConstants.VersionMajor,
            VersionMinor = ProtocolConstants.VersionMinor,
            Type = MessageType.MessageEditResponse,
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

// ===================== MESSAGE DELETE HANDLER =====================

public class MessageDeleteHandler : IMessageHandler
{
    public MessageType Type => MessageType.MessageDelete;

    private readonly IMessageService _messageService;
    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;
    private readonly ILogger<MessageDeleteHandler> _logger;

    public MessageDeleteHandler(
        IMessageService messageService,
        SessionManager sessionManager,
        IMessageSender messageSender,
        ILogger<MessageDeleteHandler> logger)
    {
        _messageService = messageService;
        _sessionManager = sessionManager;
        _messageSender = messageSender;
        _logger = logger;
    }

    public async ValueTask HandleAsync(ConnectionContext context, Message message)
    {
        try
        {
            var session = _sessionManager.GetAuthenticatedSession(context.ConnectionId);
            if (session == null)
            {
                await SendResponseAsync(context, message.SequenceId, new MessageDeleteResponse(false, "Not authenticated"));
                return;
            }

            var request = PayloadSerializer.Deserialize<MessageDeleteRequest>(message.Payload);
            if (request == null)
            {
                await SendResponseAsync(context, message.SequenceId, new MessageDeleteResponse(false, "Invalid payload"));
                return;
            }

            bool deleted;
            switch (request.Scope)
            {
                case "private":
                    deleted = await _messageService.DeleteMessageAsync(request.MessageId, session.UserId);
                    break;

                case "channel" when request.ChannelId.HasValue:
                    deleted = await _messageService.DeleteChannelMessageAsync(
                        request.MessageId, session.UserId, request.ChannelId.Value);
                    break;

                case "group" when request.GroupId.HasValue:
                    deleted = await _messageService.DeleteGroupMessageAsync(
                        request.MessageId, session.UserId, request.GroupId.Value);
                    break;

                default:
                    await SendResponseAsync(context, message.SequenceId, 
                        new MessageDeleteResponse(false, "Invalid scope or missing ID"));
                    return;
            }

            if (deleted)
            {
                await SendResponseAsync(context, message.SequenceId, 
                    new MessageDeleteResponse(true, "Message deleted", request.MessageId));
                _logger.LogInformation("Message {MessageId} deleted by user {UserId} in scope {Scope}", 
                    request.MessageId, session.UserId, request.Scope);
            }
            else
            {
                await SendResponseAsync(context, message.SequenceId, 
                    new MessageDeleteResponse(false, "Message not found or no permission to delete"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogHandlerError(ex, "message_delete", context, message, _sessionManager);
            await SendResponseAsync(context, message.SequenceId, new MessageDeleteResponse(false, "Internal server error"));
        }
    }

    private async Task SendResponseAsync(ConnectionContext context, ulong sequenceId, MessageDeleteResponse response)
    {
        var payload = PayloadSerializer.Serialize(response);
        var msg = new Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = ProtocolConstants.VersionMajor,
            VersionMinor = ProtocolConstants.VersionMinor,
            Type = MessageType.MessageDeleteResponse,
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
