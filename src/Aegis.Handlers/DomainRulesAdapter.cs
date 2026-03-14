using Aegis.DomainRules;
using Aegis.Data.Entities;
using Aegis.Data.Services;

namespace Aegis.Handlers;

public enum AdminTargetScope
{
    Channel = 1,
    Group = 2
}

public sealed class DomainRulesAdapter
{
    private readonly IMessageDomainRules _domainRules;

    public DomainRulesAdapter(IMessageDomainRules domainRules)
    {
        _domainRules = domainRules;
    }

    public bool TryValidateMessageSend(
        string scope,
        ulong targetId,
        ulong senderUserId,
        string? content,
        int attachmentCount,
        int requestedContentType,
        out string? error)
    {
        var decision = _domainRules.ValidateMessageSend(new MessageRuleContext
        {
            Scope = scope,
            TargetId = targetId,
            SenderUserId = senderUserId,
            Content = content,
            AttachmentCount = attachmentCount,
            RequestedContentType = requestedContentType
        });

        error = decision.ErrorMessage;
        return decision.IsAllowed;
    }

    public bool TryValidateRoleUpdate(
        string? scope,
        ulong targetId,
        ulong actorUserId,
        ulong targetUserId,
        int newRole,
        out string? error)
    {
        var decision = _domainRules.ValidateRoleUpdate(new RoleUpdateRuleContext
        {
            Scope = scope ?? string.Empty,
            TargetId = targetId,
            ActorUserId = actorUserId,
            TargetUserId = targetUserId,
            NewRole = newRole
        });

        error = decision.ErrorMessage;
        return decision.IsAllowed;
    }

    public bool TryValidatePermissionUpdate(
        string? scope,
        ulong targetId,
        ulong actorUserId,
        ulong targetUserId,
        out string? error)
    {
        var decision = _domainRules.ValidatePermissionUpdate(new PermissionUpdateRuleContext
        {
            Scope = scope ?? string.Empty,
            TargetId = targetId,
            ActorUserId = actorUserId,
            TargetUserId = targetUserId
        });

        error = decision.ErrorMessage;
        return decision.IsAllowed;
    }

    public bool TryResolveAdminScope(string? scope, out AdminTargetScope resolvedScope, out string? error)
    {
        var normalized = (scope ?? string.Empty).Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "channel":
                resolvedScope = AdminTargetScope.Channel;
                error = null;
                return true;
            case "group":
                resolvedScope = AdminTargetScope.Group;
                error = null;
                return true;
            default:
                resolvedScope = default;
                error = "Invalid scope";
                return false;
        }
    }

    public MemberPermissions BuildMemberPermissions(
        bool? canSendMessages,
        bool? canDeleteOthersMessages,
        bool? canEditInfo,
        bool? canInviteUsers,
        bool? canRemoveUsers,
        bool? canPinMessages,
        bool? canManageRoles)
    {
        return new MemberPermissions(
            canSendMessages,
            canDeleteOthersMessages,
            canEditInfo,
            canInviteUsers,
            canRemoveUsers,
            canPinMessages,
            canManageRoles);
    }

    public Task ApplyRoleUpdateAsync(
        AdminTargetScope scope,
        IChannelService channelService,
        IGroupService groupService,
        ulong targetId,
        ulong actorUserId,
        ulong targetUserId,
        int newRole)
    {
        return scope switch
        {
            AdminTargetScope.Channel => channelService.UpdateMemberRoleAsync(
                targetId,
                actorUserId,
                targetUserId,
                (ChannelMemberRole)newRole),
            AdminTargetScope.Group => groupService.UpdateMemberRoleAsync(
                targetId,
                actorUserId,
                targetUserId,
                (GroupMemberRole)newRole),
            _ => Task.CompletedTask
        };
    }

    public Task ApplyPermissionUpdateAsync(
        AdminTargetScope scope,
        IChannelService channelService,
        IGroupService groupService,
        ulong targetId,
        ulong actorUserId,
        ulong targetUserId,
        MemberPermissions permissions)
    {
        return scope switch
        {
            AdminTargetScope.Channel => channelService.UpdateMemberPermissionsAsync(
                targetId,
                actorUserId,
                targetUserId,
                permissions),
            AdminTargetScope.Group => groupService.UpdateMemberPermissionsAsync(
                targetId,
                actorUserId,
                targetUserId,
                permissions),
            _ => Task.CompletedTask
        };
    }
}
