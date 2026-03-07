using System.Text.Json;
using Aegis.Common;
using Aegis.Data.Services;
using Aegis.Protocol;
using Aegis.Transport;
using Microsoft.Extensions.Logging;

namespace Aegis.Handlers;

// ===================== PAYLOADS =====================

public record ChannelEditRequest(
    ulong ChannelId,
    string? Name = null,
    string? Description = null,
    string? AvatarUrl = null
);

public record ChannelEditResponse(
    bool Success,
    string? Message = null
);

public record GroupCreateRequest(
    string Name,
    string? Description = null
);

public record GroupCreateResponse(
    bool Success,
    ulong GroupId = 0,
    string? Message = null
);

public record GroupEditRequest(
    ulong GroupId,
    string? Name = null,
    string? Description = null,
    string? AvatarUrl = null
);

public record GroupEditResponse(
    bool Success,
    string? Message = null
);

public record GroupMessageSendRequest(
    ulong GroupId,
    string Content,
    Aegis.Data.Entities.MessageContentType ContentType = Aegis.Data.Entities.MessageContentType.Text,
    ulong? ReplyToMessageId = null
);

public record GroupMessageSendResponse(
    bool Success,
    ulong MessageId = 0,
    string? Message = null
);

public record MemberRoleUpdateRequest(
    string Scope, // "channel" or "group"
    ulong TargetId, // channel or group ID
    ulong TargetUserId,
    int NewRole // ChannelMemberRole or GroupMemberRole enum value
);

public record MemberRoleUpdateResponse(
    bool Success,
    string? Message = null
);

public record MemberPermissionUpdateRequest(
    string Scope, // "channel" or "group"
    ulong TargetId,
    ulong TargetUserId,
    bool? CanSendMessages = null,
    bool? CanDeleteOthersMessages = null,
    bool? CanEditInfo = null,
    bool? CanInviteUsers = null,
    bool? CanRemoveUsers = null,
    bool? CanPinMessages = null,
    bool? CanManageRoles = null
);

public record MemberPermissionUpdateResponse(
    bool Success,
    string? Message = null
);

// ===================== CHANNEL EDIT HANDLER =====================

public class ChannelEditHandler : IMessageHandler
{
    public MessageType Type => MessageType.ChannelEdit;

    private readonly IChannelService _channelService;
    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;
    private readonly ILogger<ChannelEditHandler> _logger;

    public ChannelEditHandler(
        IChannelService channelService,
        SessionManager sessionManager,
        IMessageSender messageSender,
        ILogger<ChannelEditHandler> logger)
    {
        _channelService = channelService;
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
                await SendResponseAsync(context, message.SequenceId, new ChannelEditResponse(false, "Not authenticated"));
                return;
            }

            var request = JsonSerializer.Deserialize<ChannelEditRequest>(message.Payload);
            if (request == null)
            {
                await SendResponseAsync(context, message.SequenceId, new ChannelEditResponse(false, "Invalid payload"));
                return;
            }

            await _channelService.UpdateChannelAsync(
                request.ChannelId, session.UserId,
                request.Name, request.Description, request.AvatarUrl);

            await SendResponseAsync(context, message.SequenceId, new ChannelEditResponse(true, "Channel updated"));
            _logger.LogInformation("Channel {ChannelId} edited by user {UserId}", request.ChannelId, session.UserId);
        }
        catch (UnauthorizedAccessException ex)
        {
            await SendResponseAsync(context, message.SequenceId, new ChannelEditResponse(false, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            await SendResponseAsync(context, message.SequenceId, new ChannelEditResponse(false, ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error editing channel");
            await SendResponseAsync(context, message.SequenceId, new ChannelEditResponse(false, "Internal server error"));
        }
    }

    private async Task SendResponseAsync(ConnectionContext context, ulong sequenceId, ChannelEditResponse response)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(response);
        var msg = new Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = ProtocolConstants.VersionMajor,
            VersionMinor = ProtocolConstants.VersionMinor,
            Type = MessageType.ChannelEditResponse,
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

// ===================== GROUP CREATE HANDLER =====================

public class GroupCreateHandler : IMessageHandler
{
    public MessageType Type => MessageType.GroupCreate;

    private readonly IGroupService _groupService;
    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;
    private readonly ILogger<GroupCreateHandler> _logger;

    public GroupCreateHandler(
        IGroupService groupService,
        SessionManager sessionManager,
        IMessageSender messageSender,
        ILogger<GroupCreateHandler> logger)
    {
        _groupService = groupService;
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
                await SendResponseAsync(context, message.SequenceId, new GroupCreateResponse(false, Message: "Not authenticated"));
                return;
            }

            var request = JsonSerializer.Deserialize<GroupCreateRequest>(message.Payload);
            if (request == null)
            {
                await SendResponseAsync(context, message.SequenceId, new GroupCreateResponse(false, Message: "Invalid payload"));
                return;
            }

            var group = await _groupService.CreateGroupAsync(session.UserId, request.Name, request.Description);
            await SendResponseAsync(context, message.SequenceId, 
                new GroupCreateResponse(true, group.Id, "Group created"));

            _logger.LogInformation("Group '{GroupName}' created by user {UserId}", request.Name, session.UserId);
        }
        catch (ArgumentException ex)
        {
            await SendResponseAsync(context, message.SequenceId, new GroupCreateResponse(false, Message: ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating group");
            await SendResponseAsync(context, message.SequenceId, new GroupCreateResponse(false, Message: "Internal server error"));
        }
    }

    private async Task SendResponseAsync(ConnectionContext context, ulong sequenceId, GroupCreateResponse response)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(response);
        var msg = new Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = ProtocolConstants.VersionMajor,
            VersionMinor = ProtocolConstants.VersionMinor,
            Type = MessageType.GroupCreateResponse,
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

// ===================== GROUP EDIT HANDLER =====================

public class GroupEditHandler : IMessageHandler
{
    public MessageType Type => MessageType.GroupEdit;

    private readonly IGroupService _groupService;
    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;
    private readonly ILogger<GroupEditHandler> _logger;

    public GroupEditHandler(
        IGroupService groupService,
        SessionManager sessionManager,
        IMessageSender messageSender,
        ILogger<GroupEditHandler> logger)
    {
        _groupService = groupService;
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
                await SendResponseAsync(context, message.SequenceId, new GroupEditResponse(false, "Not authenticated"));
                return;
            }

            var request = JsonSerializer.Deserialize<GroupEditRequest>(message.Payload);
            if (request == null)
            {
                await SendResponseAsync(context, message.SequenceId, new GroupEditResponse(false, "Invalid payload"));
                return;
            }

            await _groupService.UpdateGroupAsync(
                request.GroupId, session.UserId,
                request.Name, request.Description, request.AvatarUrl);

            await SendResponseAsync(context, message.SequenceId, new GroupEditResponse(true, "Group updated"));
            _logger.LogInformation("Group {GroupId} edited by user {UserId}", request.GroupId, session.UserId);
        }
        catch (UnauthorizedAccessException ex)
        {
            await SendResponseAsync(context, message.SequenceId, new GroupEditResponse(false, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            await SendResponseAsync(context, message.SequenceId, new GroupEditResponse(false, ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error editing group");
            await SendResponseAsync(context, message.SequenceId, new GroupEditResponse(false, "Internal server error"));
        }
    }

    private async Task SendResponseAsync(ConnectionContext context, ulong sequenceId, GroupEditResponse response)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(response);
        var msg = new Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = ProtocolConstants.VersionMajor,
            VersionMinor = ProtocolConstants.VersionMinor,
            Type = MessageType.GroupEditResponse,
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

// ===================== GROUP MESSAGE HANDLER =====================

public class GroupMessageSendHandler : IMessageHandler
{
    public MessageType Type => MessageType.GroupMessageSend;

    private readonly IMessageService _messageService;
    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;
    private readonly ILogger<GroupMessageSendHandler> _logger;

    public GroupMessageSendHandler(
        IMessageService messageService,
        SessionManager sessionManager,
        IMessageSender messageSender,
        ILogger<GroupMessageSendHandler> logger)
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
                await SendResponseAsync(context, message.SequenceId, new GroupMessageSendResponse(false, Message: "Not authenticated"));
                return;
            }

            var request = JsonSerializer.Deserialize<GroupMessageSendRequest>(message.Payload);
            if (request == null)
            {
                await SendResponseAsync(context, message.SequenceId, new GroupMessageSendResponse(false, Message: "Invalid payload"));
                return;
            }

            var msg = await _messageService.SendGroupMessageAsync(
                request.GroupId, session.UserId, request.Content,
                request.ContentType, request.ReplyToMessageId);

            await SendResponseAsync(context, message.SequenceId, 
                new GroupMessageSendResponse(true, msg.Id, "Message sent"));

            _logger.LogInformation("Group message sent to group {GroupId} by user {UserId}", request.GroupId, session.UserId);
        }
        catch (UnauthorizedAccessException ex)
        {
            await SendResponseAsync(context, message.SequenceId, new GroupMessageSendResponse(false, Message: ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            await SendResponseAsync(context, message.SequenceId, new GroupMessageSendResponse(false, Message: ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending group message");
            await SendResponseAsync(context, message.SequenceId, new GroupMessageSendResponse(false, Message: "Internal server error"));
        }
    }

    private async Task SendResponseAsync(ConnectionContext context, ulong sequenceId, GroupMessageSendResponse response)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(response);
        var msg = new Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = ProtocolConstants.VersionMajor,
            VersionMinor = ProtocolConstants.VersionMinor,
            Type = MessageType.GroupMessageResponse,
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

// ===================== MEMBER ROLE UPDATE HANDLER =====================

public class MemberRoleUpdateHandler : IMessageHandler
{
    public MessageType Type => MessageType.MemberRoleUpdate;

    private readonly IChannelService _channelService;
    private readonly IGroupService _groupService;
    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;
    private readonly ILogger<MemberRoleUpdateHandler> _logger;

    public MemberRoleUpdateHandler(
        IChannelService channelService,
        IGroupService groupService,
        SessionManager sessionManager,
        IMessageSender messageSender,
        ILogger<MemberRoleUpdateHandler> logger)
    {
        _channelService = channelService;
        _groupService = groupService;
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
                await SendResponseAsync(context, message.SequenceId, new MemberRoleUpdateResponse(false, "Not authenticated"));
                return;
            }

            var request = JsonSerializer.Deserialize<MemberRoleUpdateRequest>(message.Payload);
            if (request == null)
            {
                await SendResponseAsync(context, message.SequenceId, new MemberRoleUpdateResponse(false, "Invalid payload"));
                return;
            }

            switch (request.Scope)
            {
                case "channel":
                    await _channelService.UpdateMemberRoleAsync(
                        request.TargetId, session.UserId, request.TargetUserId,
                        (Aegis.Data.Entities.ChannelMemberRole)request.NewRole);
                    break;

                case "group":
                    await _groupService.UpdateMemberRoleAsync(
                        request.TargetId, session.UserId, request.TargetUserId,
                        (Aegis.Data.Entities.GroupMemberRole)request.NewRole);
                    break;

                default:
                    await SendResponseAsync(context, message.SequenceId, new MemberRoleUpdateResponse(false, "Invalid scope"));
                    return;
            }

            await SendResponseAsync(context, message.SequenceId, new MemberRoleUpdateResponse(true, "Role updated"));
            _logger.LogInformation("Role updated for user {TargetUserId} in {Scope} {TargetId} to {NewRole} by {ActorUserId}",
                request.TargetUserId, request.Scope, request.TargetId, request.NewRole, session.UserId);
        }
        catch (UnauthorizedAccessException ex)
        {
            await SendResponseAsync(context, message.SequenceId, new MemberRoleUpdateResponse(false, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            await SendResponseAsync(context, message.SequenceId, new MemberRoleUpdateResponse(false, ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating member role");
            await SendResponseAsync(context, message.SequenceId, new MemberRoleUpdateResponse(false, "Internal server error"));
        }
    }

    private async Task SendResponseAsync(ConnectionContext context, ulong sequenceId, MemberRoleUpdateResponse response)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(response);
        var msg = new Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = ProtocolConstants.VersionMajor,
            VersionMinor = ProtocolConstants.VersionMinor,
            Type = MessageType.MemberRoleUpdateResponse,
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

// ===================== MEMBER PERMISSION UPDATE HANDLER =====================

public class MemberPermissionUpdateHandler : IMessageHandler
{
    public MessageType Type => MessageType.MemberPermissionUpdate;

    private readonly IChannelService _channelService;
    private readonly IGroupService _groupService;
    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;
    private readonly ILogger<MemberPermissionUpdateHandler> _logger;

    public MemberPermissionUpdateHandler(
        IChannelService channelService,
        IGroupService groupService,
        SessionManager sessionManager,
        IMessageSender messageSender,
        ILogger<MemberPermissionUpdateHandler> logger)
    {
        _channelService = channelService;
        _groupService = groupService;
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
                await SendResponseAsync(context, message.SequenceId, new MemberPermissionUpdateResponse(false, "Not authenticated"));
                return;
            }

            var request = JsonSerializer.Deserialize<MemberPermissionUpdateRequest>(message.Payload);
            if (request == null)
            {
                await SendResponseAsync(context, message.SequenceId, new MemberPermissionUpdateResponse(false, "Invalid payload"));
                return;
            }

            var permissions = new MemberPermissions(
                request.CanSendMessages,
                request.CanDeleteOthersMessages,
                request.CanEditInfo,
                request.CanInviteUsers,
                request.CanRemoveUsers,
                request.CanPinMessages,
                request.CanManageRoles);

            switch (request.Scope)
            {
                case "channel":
                    await _channelService.UpdateMemberPermissionsAsync(
                        request.TargetId, session.UserId, request.TargetUserId, permissions);
                    break;

                case "group":
                    await _groupService.UpdateMemberPermissionsAsync(
                        request.TargetId, session.UserId, request.TargetUserId, permissions);
                    break;

                default:
                    await SendResponseAsync(context, message.SequenceId, new MemberPermissionUpdateResponse(false, "Invalid scope"));
                    return;
            }

            await SendResponseAsync(context, message.SequenceId, new MemberPermissionUpdateResponse(true, "Permissions updated"));
            _logger.LogInformation("Permissions updated for user {TargetUserId} in {Scope} {TargetId} by {ActorUserId}",
                request.TargetUserId, request.Scope, request.TargetId, session.UserId);
        }
        catch (UnauthorizedAccessException ex)
        {
            await SendResponseAsync(context, message.SequenceId, new MemberPermissionUpdateResponse(false, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            await SendResponseAsync(context, message.SequenceId, new MemberPermissionUpdateResponse(false, ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating member permissions");
            await SendResponseAsync(context, message.SequenceId, new MemberPermissionUpdateResponse(false, "Internal server error"));
        }
    }

    private async Task SendResponseAsync(ConnectionContext context, ulong sequenceId, MemberPermissionUpdateResponse response)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(response);
        var msg = new Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = ProtocolConstants.VersionMajor,
            VersionMinor = ProtocolConstants.VersionMinor,
            Type = MessageType.MemberPermissionUpdateResponse,
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
