using Aegis.Common;
using Aegis.Data.Entities;
using Aegis.Data.Repositories;
using Aegis.Data.Services;
using Aegis.Protocol;
using Aegis.Transport;
using Microsoft.Extensions.Logging;
// Disambiguate Aegis.Data.Entities.Message vs Aegis.Protocol.Message
using Message = Aegis.Protocol.Message;

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
    string? Content,
    Aegis.Data.Entities.MessageContentType ContentType = Aegis.Data.Entities.MessageContentType.Text,
    ulong? ReplyToMessageId = null,
    MediaAttachmentPayload? Attachment = null,
    IReadOnlyList<MediaAttachmentPayload>? Attachments = null,
    string? ParseMode = null
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

// SERVER-002: group message event payload pushed to members
public record GroupMessageEventPayload(
    ulong Id,
    ulong GroupId,
    ulong FromUserId,
    string Content,
    Aegis.Data.Entities.MessageContentType ContentType,
    DateTime CreatedAt,
    string? FromUsername = null,
    string? GroupName = null
);

// SERVER-003: member listing payloads
public record ChannelMembersRequest(ulong ChannelId);
public record GroupMembersRequest(ulong GroupId);

public record MemberSummary(
    ulong UserId,
    string Username,
    string Role,
    DateTime JoinedAt,
    bool CanSendMessages,
    bool CanDeleteOthersMessages,
    bool CanPinMessages,
    bool CanManageRoles
);

public record ChannelMembersResponse(
    bool Success,
    ulong ChannelId,
    IReadOnlyList<MemberSummary> Members,
    string? Message = null
);

public record GroupMembersResponse(
    bool Success,
    ulong GroupId,
    IReadOnlyList<MemberSummary> Members,
    string? Message = null
);

// SERVER-004: leave payloads
public record ChannelLeaveRequest(ulong ChannelId);
public record ChannelLeaveResponse(bool Success, string? Message = null);
public record GroupLeaveRequest(ulong GroupId);
public record GroupLeaveResponse(bool Success, string? Message = null);

// SERVER-005: reaction payloads
public record MessageReactRequest(
    string Scope,   // "private", "channel", "group"
    ulong MessageId,
    string Emoji,
    bool Remove = false
);

public record MessageReactResponse(
    bool Success,
    string? Message = null,
    IReadOnlyList<ReactionCount>? Reactions = null
);

public record ReactionCount(string Emoji, int Count, bool ByMe);

public record MessageReactionEventPayload(
    string Scope,
    ulong MessageId,
    ulong UserId,
    string Emoji,
    bool Removed,
    IReadOnlyList<ReactionCount> Reactions
);

// SERVER-005: pin payloads
public record MessagePinRequest(
    string Scope,   // "channel" or "group"
    ulong MessageId,
    ulong TargetId, // channelId or groupId
    bool Unpin = false
);

public record MessagePinResponse(bool Success, string? Message = null);

public record MessagePinEventPayload(
    string Scope,
    ulong MessageId,
    ulong TargetId,
    bool Pinned,
    ulong ActorUserId
);

// SERVER-006: room settings payloads
public record RoomSettingsGetRequest(
    string Scope,   // "channel" or "group"
    ulong TargetId
);

public record RoomSettingsGetResponse(
    bool Success,
    string Scope,
    ulong TargetId,
    int JoinRule,
    int HistoryVisibility,
    string? Message = null
);

public record RoomSettingsUpdateRequest(
    string Scope,
    ulong TargetId,
    int? JoinRule = null,
    int? HistoryVisibility = null
);

public record RoomSettingsUpdateResponse(bool Success, string? Message = null);

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

            var request = PayloadSerializer.Deserialize<ChannelEditRequest>(message.Payload);
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
            _logger.LogHandlerError(ex, "admin_channel_edit", context, message, _sessionManager);
            await SendResponseAsync(context, message.SequenceId, new ChannelEditResponse(false, "Internal server error"));
        }
    }

    private async Task SendResponseAsync(ConnectionContext context, ulong sequenceId, ChannelEditResponse response)
    {
        await HandlerResponseSender.SendAsync(
            _messageSender,
            context,
            MessageType.ChannelEditResponse,
            sequenceId,
            response);
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

            var request = PayloadSerializer.Deserialize<GroupCreateRequest>(message.Payload);
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
            _logger.LogHandlerError(ex, "admin_group_create", context, message, _sessionManager);
            await SendResponseAsync(context, message.SequenceId, new GroupCreateResponse(false, Message: "Internal server error"));
        }
    }

    private async Task SendResponseAsync(ConnectionContext context, ulong sequenceId, GroupCreateResponse response)
    {
        await HandlerResponseSender.SendAsync(
            _messageSender,
            context,
            MessageType.GroupCreateResponse,
            sequenceId,
            response);
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

            var request = PayloadSerializer.Deserialize<GroupEditRequest>(message.Payload);
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
            _logger.LogHandlerError(ex, "admin_group_edit", context, message, _sessionManager);
            await SendResponseAsync(context, message.SequenceId, new GroupEditResponse(false, "Internal server error"));
        }
    }

    private async Task SendResponseAsync(ConnectionContext context, ulong sequenceId, GroupEditResponse response)
    {
        await HandlerResponseSender.SendAsync(
            _messageSender,
            context,
            MessageType.GroupEditResponse,
            sequenceId,
            response);
    }
}

// ===================== GROUP MESSAGE HANDLER =====================

public class GroupMessageSendHandler : IMessageHandler
{
    public MessageType Type => MessageType.GroupMessageSend;

    private readonly IMessageService _messageService;
    private readonly IGroupRepository _groupRepository;
    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;
    private readonly DomainRulesAdapter _domainRules;
    private readonly ILogger<GroupMessageSendHandler> _logger;

    public GroupMessageSendHandler(
        IMessageService messageService,
        IGroupRepository groupRepository,
        SessionManager sessionManager,
        IMessageSender messageSender,
        DomainRulesAdapter domainRules,
        ILogger<GroupMessageSendHandler> logger)
    {
        _messageService = messageService;
        _groupRepository = groupRepository;
        _sessionManager = sessionManager;
        _messageSender = messageSender;
        _domainRules = domainRules;
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

            var request = PayloadSerializer.Deserialize<GroupMessageSendRequest>(message.Payload);
            if (request == null)
            {
                await SendResponseAsync(context, message.SequenceId, new GroupMessageSendResponse(false, Message: "Invalid payload"));
                return;
            }

            if (!_domainRules.TryValidateMessageSend(
                scope: "group",
                targetId: request.GroupId,
                senderUserId: session.UserId,
                content: request.Content,
                attachmentCount: MediaPayloadBuilder.GetNormalizedAttachmentCount(request.Attachment, request.Attachments),
                requestedContentType: (int)request.ContentType,
                out var ruleError))
            {
                await SendResponseAsync(context, message.SequenceId, new GroupMessageSendResponse(false, Message: ruleError ?? "Message violates domain rules"));
                return;
            }

            var contentType = MediaPayloadBuilder.ResolveContentType(request.ContentType, request.Attachment, request.Attachments);
            var normalizedContent = MediaPayloadBuilder.BuildMessageContent(request.Content, request.Attachment, request.Attachments, request.ParseMode);

            var msg = await _messageService.SendGroupMessageAsync(
                request.GroupId, session.UserId, normalizedContent,
                contentType, request.ReplyToMessageId);

            // SERVER-002: push GroupMessageEvent to all online members
            var group = await _groupRepository.GetByIdAsync(request.GroupId);
            var groupMembers = await _groupRepository.GetGroupMembersAsync(request.GroupId);

            var eventPayload = PayloadSerializer.Serialize(new GroupMessageEventPayload(
                Id: msg.Id,
                GroupId: request.GroupId,
                FromUserId: session.UserId,
                Content: msg.Content,
                ContentType: msg.ContentType,
                CreatedAt: msg.CreatedAt,
                FromUsername: session.Username,
                GroupName: group?.Name));

            foreach (var member in groupMembers)
            {
                if (member.UserId == session.UserId) continue;
                if (!_sessionManager.TryGetConnectionIdByUserId(member.UserId, out var recipientConnId)) continue;
                await _messageSender.SendProtocolMessageAsync(
                    recipientConnId,
                    (ushort)MessageType.GroupMessageEvent,
                    0,
                    eventPayload);
            }

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
            _logger.LogHandlerError(ex, "admin_group_message_send", context, message, _sessionManager);
            await SendResponseAsync(context, message.SequenceId, new GroupMessageSendResponse(false, Message: "Internal server error"));
        }
    }

    private async Task SendResponseAsync(ConnectionContext context, ulong sequenceId, GroupMessageSendResponse response)
    {
        await HandlerResponseSender.SendAsync(
            _messageSender,
            context,
            MessageType.GroupMessageResponse,
            sequenceId,
            response);
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
    private readonly DomainRulesAdapter _domainRules;
    private readonly ILogger<MemberRoleUpdateHandler> _logger;

    public MemberRoleUpdateHandler(
        IChannelService channelService,
        IGroupService groupService,
        SessionManager sessionManager,
        IMessageSender messageSender,
        DomainRulesAdapter domainRules,
        ILogger<MemberRoleUpdateHandler> logger)
    {
        _channelService = channelService;
        _groupService = groupService;
        _sessionManager = sessionManager;
        _messageSender = messageSender;
        _domainRules = domainRules;
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

            var request = PayloadSerializer.Deserialize<MemberRoleUpdateRequest>(message.Payload);
            if (request == null)
            {
                await SendResponseAsync(context, message.SequenceId, new MemberRoleUpdateResponse(false, "Invalid payload"));
                return;
            }

            if (!_domainRules.TryValidateRoleUpdate(
                scope: request.Scope,
                targetId: request.TargetId,
                actorUserId: session.UserId,
                targetUserId: request.TargetUserId,
                newRole: request.NewRole,
                out var ruleError))
            {
                await SendResponseAsync(context, message.SequenceId, new MemberRoleUpdateResponse(false, ruleError ?? "Role update violates domain rules"));
                return;
            }

            if (!_domainRules.TryResolveAdminScope(request.Scope, out var resolvedScope, out var scopeError))
            {
                await SendResponseAsync(context, message.SequenceId, new MemberRoleUpdateResponse(false, scopeError ?? "Invalid scope"));
                return;
            }

            await _domainRules.ApplyRoleUpdateAsync(
                resolvedScope,
                _channelService,
                _groupService,
                request.TargetId,
                session.UserId,
                request.TargetUserId,
                request.NewRole);

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
            _logger.LogHandlerError(ex, "admin_member_role_update", context, message, _sessionManager);
            await SendResponseAsync(context, message.SequenceId, new MemberRoleUpdateResponse(false, "Internal server error"));
        }
    }

    private async Task SendResponseAsync(ConnectionContext context, ulong sequenceId, MemberRoleUpdateResponse response)
    {
        await HandlerResponseSender.SendAsync(
            _messageSender,
            context,
            MessageType.MemberRoleUpdateResponse,
            sequenceId,
            response);
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
    private readonly DomainRulesAdapter _domainRules;
    private readonly ILogger<MemberPermissionUpdateHandler> _logger;

    public MemberPermissionUpdateHandler(
        IChannelService channelService,
        IGroupService groupService,
        SessionManager sessionManager,
        IMessageSender messageSender,
        DomainRulesAdapter domainRules,
        ILogger<MemberPermissionUpdateHandler> logger)
    {
        _channelService = channelService;
        _groupService = groupService;
        _sessionManager = sessionManager;
        _messageSender = messageSender;
        _domainRules = domainRules;
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

            var request = PayloadSerializer.Deserialize<MemberPermissionUpdateRequest>(message.Payload);
            if (request == null)
            {
                await SendResponseAsync(context, message.SequenceId, new MemberPermissionUpdateResponse(false, "Invalid payload"));
                return;
            }

            if (!_domainRules.TryValidatePermissionUpdate(
                scope: request.Scope,
                targetId: request.TargetId,
                actorUserId: session.UserId,
                targetUserId: request.TargetUserId,
                out var ruleError))
            {
                await SendResponseAsync(context, message.SequenceId, new MemberPermissionUpdateResponse(false, ruleError ?? "Permission update violates domain rules"));
                return;
            }

            var permissions = _domainRules.BuildMemberPermissions(
                request.CanSendMessages,
                request.CanDeleteOthersMessages,
                request.CanEditInfo,
                request.CanInviteUsers,
                request.CanRemoveUsers,
                request.CanPinMessages,
                request.CanManageRoles);

            if (!_domainRules.TryResolveAdminScope(request.Scope, out var resolvedScope, out var scopeError))
            {
                await SendResponseAsync(context, message.SequenceId, new MemberPermissionUpdateResponse(false, scopeError ?? "Invalid scope"));
                return;
            }

            await _domainRules.ApplyPermissionUpdateAsync(
                resolvedScope,
                _channelService,
                _groupService,
                request.TargetId,
                session.UserId,
                request.TargetUserId,
                permissions);

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
            _logger.LogHandlerError(ex, "admin_member_permissions_update", context, message, _sessionManager);
            await SendResponseAsync(context, message.SequenceId, new MemberPermissionUpdateResponse(false, "Internal server error"));
        }
    }

    private async Task SendResponseAsync(ConnectionContext context, ulong sequenceId, MemberPermissionUpdateResponse response)
    {
        await HandlerResponseSender.SendAsync(
            _messageSender,
            context,
            MessageType.MemberPermissionUpdateResponse,
            sequenceId,
            response);
    }
}

// ===================== CHANNEL MEMBERS HANDLER (SERVER-003) =====================

public class ChannelMembersHandler : IMessageHandler
{
    public MessageType Type => MessageType.ChannelMembersRequest;

    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;
    private readonly IChannelRepository _channelRepository;
    private readonly ILogger<ChannelMembersHandler> _logger;

    public ChannelMembersHandler(
        SessionManager sessionManager,
        IMessageSender messageSender,
        IChannelRepository channelRepository,
        ILogger<ChannelMembersHandler> logger)
    {
        _sessionManager = sessionManager;
        _messageSender = messageSender;
        _channelRepository = channelRepository;
        _logger = logger;
    }

    public async ValueTask HandleAsync(ConnectionContext context, Message message)
    {
        try
        {
            var session = _sessionManager.GetAuthenticatedSession(context.ConnectionId);
            if (session == null)
            {
                await SendResponseAsync(context, message.SequenceId, new ChannelMembersResponse(false, 0, Array.Empty<MemberSummary>(), "Not authenticated"));
                return;
            }

            var request = PayloadSerializer.Deserialize<ChannelMembersRequest>(message.Payload);
            if (request == null || request.ChannelId == 0)
            {
                await SendResponseAsync(context, message.SequenceId, new ChannelMembersResponse(false, 0, Array.Empty<MemberSummary>(), "Invalid payload"));
                return;
            }

            var selfMember = await _channelRepository.GetChannelMemberAsync(request.ChannelId, session.UserId);
            if (selfMember == null || !selfMember.IsActive)
            {
                await SendResponseAsync(context, message.SequenceId, new ChannelMembersResponse(false, request.ChannelId, Array.Empty<MemberSummary>(), "Not a channel member"));
                return;
            }

            var members = await _channelRepository.GetChannelMembersAsync(request.ChannelId);
            var summaries = members
                .Where(m => m.IsActive)
                .Select(m => new MemberSummary(
                    UserId: m.UserId,
                    Username: m.User?.Username ?? $"user-{m.UserId}",
                    Role: m.Role.ToString(),
                    JoinedAt: m.JoinedAt,
                    CanSendMessages: m.CanSendMessages,
                    CanDeleteOthersMessages: m.CanDeleteOthersMessages,
                    CanPinMessages: m.CanPinMessages,
                    CanManageRoles: m.CanManageRoles))
                .ToList();

            await SendResponseAsync(context, message.SequenceId, new ChannelMembersResponse(true, request.ChannelId, summaries));
        }
        catch (Exception ex)
        {
            _logger.LogHandlerError(ex, "channel_members_list", context, message, _sessionManager);
            await SendResponseAsync(context, message.SequenceId, new ChannelMembersResponse(false, 0, Array.Empty<MemberSummary>(), "Internal server error"));
        }
    }

    private async Task SendResponseAsync(ConnectionContext context, ulong sequenceId, ChannelMembersResponse response)
    {
        await HandlerResponseSender.SendAsync(_messageSender, context, MessageType.ChannelMembersResponse, sequenceId, response);
    }
}

// ===================== GROUP MEMBERS HANDLER (SERVER-003) =====================

public class GroupMembersHandler : IMessageHandler
{
    public MessageType Type => MessageType.GroupMembersRequest;

    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;
    private readonly IGroupRepository _groupRepository;
    private readonly ILogger<GroupMembersHandler> _logger;

    public GroupMembersHandler(
        SessionManager sessionManager,
        IMessageSender messageSender,
        IGroupRepository groupRepository,
        ILogger<GroupMembersHandler> logger)
    {
        _sessionManager = sessionManager;
        _messageSender = messageSender;
        _groupRepository = groupRepository;
        _logger = logger;
    }

    public async ValueTask HandleAsync(ConnectionContext context, Message message)
    {
        try
        {
            var session = _sessionManager.GetAuthenticatedSession(context.ConnectionId);
            if (session == null)
            {
                await SendResponseAsync(context, message.SequenceId, new GroupMembersResponse(false, 0, Array.Empty<MemberSummary>(), "Not authenticated"));
                return;
            }

            var request = PayloadSerializer.Deserialize<GroupMembersRequest>(message.Payload);
            if (request == null || request.GroupId == 0)
            {
                await SendResponseAsync(context, message.SequenceId, new GroupMembersResponse(false, 0, Array.Empty<MemberSummary>(), "Invalid payload"));
                return;
            }

            var selfMember = await _groupRepository.GetGroupMemberAsync(request.GroupId, session.UserId);
            if (selfMember == null || !selfMember.IsActive)
            {
                await SendResponseAsync(context, message.SequenceId, new GroupMembersResponse(false, request.GroupId, Array.Empty<MemberSummary>(), "Not a group member"));
                return;
            }

            var members = await _groupRepository.GetGroupMembersAsync(request.GroupId);
            var summaries = members
                .Where(m => m.IsActive)
                .Select(m => new MemberSummary(
                    UserId: m.UserId,
                    Username: m.User?.Username ?? $"user-{m.UserId}",
                    Role: m.Role.ToString(),
                    JoinedAt: m.JoinedAt,
                    CanSendMessages: m.CanSendMessages,
                    CanDeleteOthersMessages: m.CanDeleteOthersMessages,
                    CanPinMessages: m.CanPinMessages,
                    CanManageRoles: m.CanManageRoles))
                .ToList();

            await SendResponseAsync(context, message.SequenceId, new GroupMembersResponse(true, request.GroupId, summaries));
        }
        catch (Exception ex)
        {
            _logger.LogHandlerError(ex, "group_members_list", context, message, _sessionManager);
            await SendResponseAsync(context, message.SequenceId, new GroupMembersResponse(false, 0, Array.Empty<MemberSummary>(), "Internal server error"));
        }
    }

    private async Task SendResponseAsync(ConnectionContext context, ulong sequenceId, GroupMembersResponse response)
    {
        await HandlerResponseSender.SendAsync(_messageSender, context, MessageType.GroupMembersResponse, sequenceId, response);
    }
}

// ===================== CHANNEL LEAVE HANDLER (SERVER-004) =====================

public class ChannelLeaveHandler : IMessageHandler
{
    public MessageType Type => MessageType.ChannelLeave;

    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;
    private readonly IChannelService _channelService;
    private readonly ILogger<ChannelLeaveHandler> _logger;

    public ChannelLeaveHandler(
        SessionManager sessionManager,
        IMessageSender messageSender,
        IChannelService channelService,
        ILogger<ChannelLeaveHandler> logger)
    {
        _sessionManager = sessionManager;
        _messageSender = messageSender;
        _channelService = channelService;
        _logger = logger;
    }

    public async ValueTask HandleAsync(ConnectionContext context, Message message)
    {
        try
        {
            var session = _sessionManager.GetAuthenticatedSession(context.ConnectionId);
            if (session == null)
            {
                await SendResponseAsync(context, message.SequenceId, new ChannelLeaveResponse(false, "Not authenticated"));
                return;
            }

            var request = PayloadSerializer.Deserialize<ChannelLeaveRequest>(message.Payload);
            if (request == null || request.ChannelId == 0)
            {
                await SendResponseAsync(context, message.SequenceId, new ChannelLeaveResponse(false, "Invalid payload"));
                return;
            }

            var ok = await _channelService.LeaveChannelAsync(request.ChannelId, session.UserId);
            if (!ok)
            {
                await SendResponseAsync(context, message.SequenceId, new ChannelLeaveResponse(false, "Could not leave channel"));
                return;
            }

            await SendResponseAsync(context, message.SequenceId, new ChannelLeaveResponse(true));
            _logger.LogInformation("User {UserId} left channel {ChannelId}", session.UserId, request.ChannelId);
        }
        catch (InvalidOperationException ex)
        {
            await SendResponseAsync(context, message.SequenceId, new ChannelLeaveResponse(false, ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogHandlerError(ex, "channel_leave", context, message, _sessionManager);
            await SendResponseAsync(context, message.SequenceId, new ChannelLeaveResponse(false, "Internal server error"));
        }
    }

    private async Task SendResponseAsync(ConnectionContext context, ulong sequenceId, ChannelLeaveResponse response)
    {
        await HandlerResponseSender.SendAsync(_messageSender, context, MessageType.ChannelLeave, sequenceId, response);
    }
}

// ===================== GROUP LEAVE HANDLER (SERVER-004) =====================

public class GroupLeaveHandler : IMessageHandler
{
    public MessageType Type => MessageType.GroupLeave;

    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;
    private readonly IGroupService _groupService;
    private readonly ILogger<GroupLeaveHandler> _logger;

    public GroupLeaveHandler(
        SessionManager sessionManager,
        IMessageSender messageSender,
        IGroupService groupService,
        ILogger<GroupLeaveHandler> logger)
    {
        _sessionManager = sessionManager;
        _messageSender = messageSender;
        _groupService = groupService;
        _logger = logger;
    }

    public async ValueTask HandleAsync(ConnectionContext context, Message message)
    {
        try
        {
            var session = _sessionManager.GetAuthenticatedSession(context.ConnectionId);
            if (session == null)
            {
                await SendResponseAsync(context, message.SequenceId, new GroupLeaveResponse(false, "Not authenticated"));
                return;
            }

            var request = PayloadSerializer.Deserialize<GroupLeaveRequest>(message.Payload);
            if (request == null || request.GroupId == 0)
            {
                await SendResponseAsync(context, message.SequenceId, new GroupLeaveResponse(false, "Invalid payload"));
                return;
            }

            var ok = await _groupService.LeaveGroupAsync(request.GroupId, session.UserId);
            if (!ok)
            {
                await SendResponseAsync(context, message.SequenceId, new GroupLeaveResponse(false, "Could not leave group"));
                return;
            }

            await SendResponseAsync(context, message.SequenceId, new GroupLeaveResponse(true));
            _logger.LogInformation("User {UserId} left group {GroupId}", session.UserId, request.GroupId);
        }
        catch (InvalidOperationException ex)
        {
            await SendResponseAsync(context, message.SequenceId, new GroupLeaveResponse(false, ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogHandlerError(ex, "group_leave", context, message, _sessionManager);
            await SendResponseAsync(context, message.SequenceId, new GroupLeaveResponse(false, "Internal server error"));
        }
    }

    private async Task SendResponseAsync(ConnectionContext context, ulong sequenceId, GroupLeaveResponse response)
    {
        await HandlerResponseSender.SendAsync(_messageSender, context, MessageType.GroupLeave, sequenceId, response);
    }
}

// ===================== MESSAGE REACT HANDLER (SERVER-005) =====================

public class MessageReactHandler : IMessageHandler
{
    public MessageType Type => MessageType.MessageReact;

    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;
    private readonly IChannelRepository _channelRepository;
    private readonly IGroupRepository _groupRepository;
    private readonly IReactionRepository _reactionRepository;
    private readonly ILogger<MessageReactHandler> _logger;

    public MessageReactHandler(
        SessionManager sessionManager,
        IMessageSender messageSender,
        IChannelRepository channelRepository,
        IGroupRepository groupRepository,
        IReactionRepository reactionRepository,
        ILogger<MessageReactHandler> logger)
    {
        _sessionManager = sessionManager;
        _messageSender = messageSender;
        _channelRepository = channelRepository;
        _groupRepository = groupRepository;
        _reactionRepository = reactionRepository;
        _logger = logger;
    }

    public async ValueTask HandleAsync(ConnectionContext context, Message message)
    {
        try
        {
            var session = _sessionManager.GetAuthenticatedSession(context.ConnectionId);
            if (session == null)
            {
                await SendResponseAsync(context, message.SequenceId, new MessageReactResponse(false, "Not authenticated"));
                return;
            }

            var request = PayloadSerializer.Deserialize<MessageReactRequest>(message.Payload);
            if (request == null || string.IsNullOrWhiteSpace(request.Scope) || string.IsNullOrWhiteSpace(request.Emoji))
            {
                await SendResponseAsync(context, message.SequenceId, new MessageReactResponse(false, "Invalid payload"));
                return;
            }

            bool changed;
            if (request.Remove)
                changed = await _reactionRepository.RemoveAsync(request.Scope, request.MessageId, session.UserId, request.Emoji);
            else
                changed = await _reactionRepository.AddAsync(request.Scope, request.MessageId, session.UserId, request.Emoji) != null;

            if (!changed)
            {
                await SendResponseAsync(context, message.SequenceId, new MessageReactResponse(false, "No change (already exists or not found)"));
                return;
            }

            var allReactions = await _reactionRepository.GetByMessageAsync(request.Scope, request.MessageId);
            var counts = allReactions
                .GroupBy(r => r.Emoji)
                .Select(g => new ReactionCount(g.Key, g.Count(), g.Any(r => r.UserId == session.UserId)))
                .ToList();

            await SendResponseAsync(context, message.SequenceId, new MessageReactResponse(true, null, counts));

            // Push event to all online members of the channel/group
            await PushReactionEventAsync(request, session.UserId, counts);
        }
        catch (Exception ex)
        {
            _logger.LogHandlerError(ex, "message_react", context, message, _sessionManager);
            await SendResponseAsync(context, message.SequenceId, new MessageReactResponse(false, "Internal server error"));
        }
    }

    private async Task PushReactionEventAsync(MessageReactRequest request, ulong actorUserId, IReadOnlyList<ReactionCount> counts)
    {
        var eventPayload = PayloadSerializer.Serialize(new MessageReactionEventPayload(
            Scope: request.Scope,
            MessageId: request.MessageId,
            UserId: actorUserId,
            Emoji: request.Emoji,
            Removed: request.Remove,
            Reactions: counts));

        IEnumerable<ulong> memberUserIds;
        if (request.Scope == "channel")
        {
            var channelMessage = await _channelRepository.GetChannelMessageAsync(request.MessageId);
            if (channelMessage == null)
            {
                return;
            }

            var members = await _channelRepository.GetChannelMembersAsync(channelMessage.ChannelId);
            memberUserIds = members.Where(m => m.IsActive).Select(m => m.UserId);
        }
        else if (request.Scope == "group")
        {
            var groupMessage = await _groupRepository.GetGroupMessageAsync(request.MessageId);
            if (groupMessage == null)
            {
                return;
            }

            var members = await _groupRepository.GetGroupMembersAsync(groupMessage.GroupId);
            memberUserIds = members.Where(m => m.IsActive).Select(m => m.UserId);
        }
        else
        {
            return;
        }

        foreach (var uid in memberUserIds)
        {
            if (_sessionManager.TryGetConnectionIdByUserId(uid, out var connId))
                await _messageSender.SendProtocolMessageAsync(connId, (ushort)MessageType.MessageReactionEvent, 0, eventPayload);
        }
    }

    private async Task SendResponseAsync(ConnectionContext context, ulong sequenceId, MessageReactResponse response)
    {
        await HandlerResponseSender.SendAsync(_messageSender, context, MessageType.MessageReactResponse, sequenceId, response);
    }
}

// ===================== MESSAGE PIN HANDLER (SERVER-005) =====================

public class MessagePinHandler : IMessageHandler
{
    public MessageType Type => MessageType.MessagePin;

    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;
    private readonly IChannelRepository _channelRepository;
    private readonly IGroupRepository _groupRepository;
    private readonly ILogger<MessagePinHandler> _logger;

    public MessagePinHandler(
        SessionManager sessionManager,
        IMessageSender messageSender,
        IChannelRepository channelRepository,
        IGroupRepository groupRepository,
        ILogger<MessagePinHandler> logger)
    {
        _sessionManager = sessionManager;
        _messageSender = messageSender;
        _channelRepository = channelRepository;
        _groupRepository = groupRepository;
        _logger = logger;
    }

    public async ValueTask HandleAsync(ConnectionContext context, Message message)
    {
        try
        {
            var session = _sessionManager.GetAuthenticatedSession(context.ConnectionId);
            if (session == null)
            {
                await SendResponseAsync(context, message.SequenceId, new MessagePinResponse(false, "Not authenticated"));
                return;
            }

            var request = PayloadSerializer.Deserialize<MessagePinRequest>(message.Payload);
            if (request == null || string.IsNullOrWhiteSpace(request.Scope) || request.MessageId == 0)
            {
                await SendResponseAsync(context, message.SequenceId, new MessagePinResponse(false, "Invalid payload"));
                return;
            }

            if (request.Scope == "channel")
            {
                var member = await _channelRepository.GetChannelMemberAsync(request.TargetId, session.UserId);
                if (member == null || !member.IsActive || !member.CanPinMessages)
                {
                    await SendResponseAsync(context, message.SequenceId, new MessagePinResponse(false, "Insufficient permissions"));
                    return;
                }

                var msg = await _channelRepository.GetChannelMessageAsync(request.MessageId);
                if (msg == null || msg.ChannelId != request.TargetId)
                {
                    await SendResponseAsync(context, message.SequenceId, new MessagePinResponse(false, "Message not found"));
                    return;
                }

                msg.IsPinned = !request.Unpin;
                await _channelRepository.UpdateChannelMessageAsync(msg);
                await SendResponseAsync(context, message.SequenceId, new MessagePinResponse(true));

                await PushPinEventAsync(request, session.UserId, async () =>
                {
                    var members = await _channelRepository.GetChannelMembersAsync(request.TargetId);
                    return members.Where(m => m.IsActive).Select(m => m.UserId);
                });
            }
            else if (request.Scope == "group")
            {
                var member = await _groupRepository.GetGroupMemberAsync(request.TargetId, session.UserId);
                if (member == null || !member.IsActive || !member.CanPinMessages)
                {
                    await SendResponseAsync(context, message.SequenceId, new MessagePinResponse(false, "Insufficient permissions"));
                    return;
                }

                var msg = await _groupRepository.GetGroupMessageAsync(request.MessageId);
                if (msg == null || msg.GroupId != request.TargetId)
                {
                    await SendResponseAsync(context, message.SequenceId, new MessagePinResponse(false, "Message not found"));
                    return;
                }

                msg.IsPinned = !request.Unpin;
                await _groupRepository.UpdateGroupMessageAsync(msg);
                await SendResponseAsync(context, message.SequenceId, new MessagePinResponse(true));

                await PushPinEventAsync(request, session.UserId, async () =>
                {
                    var members = await _groupRepository.GetGroupMembersAsync(request.TargetId);
                    return members.Where(m => m.IsActive).Select(m => m.UserId);
                });
            }
            else
            {
                await SendResponseAsync(context, message.SequenceId, new MessagePinResponse(false, "Invalid scope"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogHandlerError(ex, "message_pin", context, message, _sessionManager);
            await SendResponseAsync(context, message.SequenceId, new MessagePinResponse(false, "Internal server error"));
        }
    }

    private async Task PushPinEventAsync(MessagePinRequest request, ulong actorUserId, Func<Task<IEnumerable<ulong>>> getMemberIds)
    {
        var eventPayload = PayloadSerializer.Serialize(new MessagePinEventPayload(
            Scope: request.Scope,
            MessageId: request.MessageId,
            TargetId: request.TargetId,
            Pinned: !request.Unpin,
            ActorUserId: actorUserId));

        foreach (var uid in await getMemberIds())
        {
            if (_sessionManager.TryGetConnectionIdByUserId(uid, out var connId))
                await _messageSender.SendProtocolMessageAsync(connId, (ushort)MessageType.MessagePinEvent, 0, eventPayload);
        }
    }

    private async Task SendResponseAsync(ConnectionContext context, ulong sequenceId, MessagePinResponse response)
    {
        await HandlerResponseSender.SendAsync(_messageSender, context, MessageType.MessagePinResponse, sequenceId, response);
    }
}

// ===================== ROOM SETTINGS GET HANDLER (SERVER-006) =====================

public class RoomSettingsGetHandler : IMessageHandler
{
    public MessageType Type => MessageType.RoomSettingsGet;

    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;
    private readonly IChannelRepository _channelRepository;
    private readonly IGroupRepository _groupRepository;
    private readonly ILogger<RoomSettingsGetHandler> _logger;

    public RoomSettingsGetHandler(
        SessionManager sessionManager,
        IMessageSender messageSender,
        IChannelRepository channelRepository,
        IGroupRepository groupRepository,
        ILogger<RoomSettingsGetHandler> logger)
    {
        _sessionManager = sessionManager;
        _messageSender = messageSender;
        _channelRepository = channelRepository;
        _groupRepository = groupRepository;
        _logger = logger;
    }

    public async ValueTask HandleAsync(ConnectionContext context, Message message)
    {
        try
        {
            var session = _sessionManager.GetAuthenticatedSession(context.ConnectionId);
            if (session == null)
            {
                await SendResponseAsync(context, message.SequenceId, new RoomSettingsGetResponse(false, "", 0, 0, 0, "Not authenticated"));
                return;
            }

            var request = PayloadSerializer.Deserialize<RoomSettingsGetRequest>(message.Payload);
            if (request == null || string.IsNullOrWhiteSpace(request.Scope) || request.TargetId == 0)
            {
                await SendResponseAsync(context, message.SequenceId, new RoomSettingsGetResponse(false, "", 0, 0, 0, "Invalid payload"));
                return;
            }

            if (request.Scope == "channel")
            {
                var member = await _channelRepository.GetChannelMemberAsync(request.TargetId, session.UserId);
                if (member == null || !member.IsActive)
                {
                    await SendResponseAsync(context, message.SequenceId, new RoomSettingsGetResponse(false, request.Scope, request.TargetId, 0, 0, "Not a channel member"));
                    return;
                }

                var channel = await _channelRepository.GetByIdAsync(request.TargetId);
                if (channel == null)
                {
                    await SendResponseAsync(context, message.SequenceId, new RoomSettingsGetResponse(false, request.Scope, request.TargetId, 0, 0, "Channel not found"));
                    return;
                }

                await SendResponseAsync(context, message.SequenceId, new RoomSettingsGetResponse(
                    true, request.Scope, request.TargetId,
                    (int)channel.JoinRule, (int)channel.HistoryVisibility));
            }
            else if (request.Scope == "group")
            {
                var member = await _groupRepository.GetGroupMemberAsync(request.TargetId, session.UserId);
                if (member == null || !member.IsActive)
                {
                    await SendResponseAsync(context, message.SequenceId, new RoomSettingsGetResponse(false, request.Scope, request.TargetId, 0, 0, "Not a group member"));
                    return;
                }

                var group = await _groupRepository.GetByIdAsync(request.TargetId);
                if (group == null)
                {
                    await SendResponseAsync(context, message.SequenceId, new RoomSettingsGetResponse(false, request.Scope, request.TargetId, 0, 0, "Group not found"));
                    return;
                }

                await SendResponseAsync(context, message.SequenceId, new RoomSettingsGetResponse(
                    true, request.Scope, request.TargetId,
                    (int)group.JoinRule, (int)group.HistoryVisibility));
            }
            else
            {
                await SendResponseAsync(context, message.SequenceId, new RoomSettingsGetResponse(false, request.Scope, request.TargetId, 0, 0, "Invalid scope"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogHandlerError(ex, "room_settings_get", context, message, _sessionManager);
            await SendResponseAsync(context, message.SequenceId, new RoomSettingsGetResponse(false, "", 0, 0, 0, "Internal server error"));
        }
    }

    private async Task SendResponseAsync(ConnectionContext context, ulong sequenceId, RoomSettingsGetResponse response)
    {
        await HandlerResponseSender.SendAsync(_messageSender, context, MessageType.RoomSettingsGetResponse, sequenceId, response);
    }
}

// ===================== ROOM SETTINGS UPDATE HANDLER (SERVER-006) =====================

public class RoomSettingsUpdateHandler : IMessageHandler
{
    public MessageType Type => MessageType.RoomSettingsUpdate;

    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;
    private readonly IChannelService _channelService;
    private readonly IGroupService _groupService;
    private readonly ILogger<RoomSettingsUpdateHandler> _logger;

    public RoomSettingsUpdateHandler(
        SessionManager sessionManager,
        IMessageSender messageSender,
        IChannelService channelService,
        IGroupService groupService,
        ILogger<RoomSettingsUpdateHandler> logger)
    {
        _sessionManager = sessionManager;
        _messageSender = messageSender;
        _channelService = channelService;
        _groupService = groupService;
        _logger = logger;
    }

    public async ValueTask HandleAsync(ConnectionContext context, Message message)
    {
        try
        {
            var session = _sessionManager.GetAuthenticatedSession(context.ConnectionId);
            if (session == null)
            {
                await SendResponseAsync(context, message.SequenceId, new RoomSettingsUpdateResponse(false, "Not authenticated"));
                return;
            }

            var request = PayloadSerializer.Deserialize<RoomSettingsUpdateRequest>(message.Payload);
            if (request == null || string.IsNullOrWhiteSpace(request.Scope) || request.TargetId == 0)
            {
                await SendResponseAsync(context, message.SequenceId, new RoomSettingsUpdateResponse(false, "Invalid payload"));
                return;
            }

            JoinRule? joinRule = request.JoinRule.HasValue ? (JoinRule)request.JoinRule.Value : null;
            HistoryVisibility? histVis = request.HistoryVisibility.HasValue ? (HistoryVisibility)request.HistoryVisibility.Value : null;

            if (request.Scope == "channel")
            {
                await _channelService.UpdateRoomSettingsAsync(request.TargetId, session.UserId, joinRule, histVis);
                await SendResponseAsync(context, message.SequenceId, new RoomSettingsUpdateResponse(true));
                _logger.LogInformation("Room settings updated for channel {ChannelId} by user {UserId}", request.TargetId, session.UserId);
            }
            else if (request.Scope == "group")
            {
                await _groupService.UpdateRoomSettingsAsync(request.TargetId, session.UserId, joinRule, histVis);
                await SendResponseAsync(context, message.SequenceId, new RoomSettingsUpdateResponse(true));
                _logger.LogInformation("Room settings updated for group {GroupId} by user {UserId}", request.TargetId, session.UserId);
            }
            else
            {
                await SendResponseAsync(context, message.SequenceId, new RoomSettingsUpdateResponse(false, "Invalid scope"));
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            await SendResponseAsync(context, message.SequenceId, new RoomSettingsUpdateResponse(false, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            await SendResponseAsync(context, message.SequenceId, new RoomSettingsUpdateResponse(false, ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogHandlerError(ex, "room_settings_update", context, message, _sessionManager);
            await SendResponseAsync(context, message.SequenceId, new RoomSettingsUpdateResponse(false, "Internal server error"));
        }
    }

    private async Task SendResponseAsync(ConnectionContext context, ulong sequenceId, RoomSettingsUpdateResponse response)
    {
        await HandlerResponseSender.SendAsync(_messageSender, context, MessageType.RoomSettingsUpdateResponse, sequenceId, response);
    }
}
