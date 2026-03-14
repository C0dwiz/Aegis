namespace Aegis.DomainRules

open System

[<AllowNullLiteral>]
type MessageRuleContext() =
    member val Scope = "" with get, set
    member val TargetId = 0UL with get, set
    member val SenderUserId = 0UL with get, set
    member val Content : string = null with get, set
    member val AttachmentCount = 0 with get, set
    member val RequestedContentType = 0 with get, set

[<AllowNullLiteral>]
type RoleUpdateRuleContext() =
    member val Scope = "" with get, set
    member val TargetId = 0UL with get, set
    member val ActorUserId = 0UL with get, set
    member val TargetUserId = 0UL with get, set
    member val NewRole = 0 with get, set

[<AllowNullLiteral>]
type PermissionUpdateRuleContext() =
    member val Scope = "" with get, set
    member val TargetId = 0UL with get, set
    member val ActorUserId = 0UL with get, set
    member val TargetUserId = 0UL with get, set

[<AllowNullLiteral>]
type RuleDecision() =
    member val IsAllowed = false with get, set
    member val ErrorMessage : string = null with get, set


type IMessageDomainRules =
    abstract member ValidateMessageSend: MessageRuleContext -> RuleDecision
    abstract member ValidateRoleUpdate: RoleUpdateRuleContext -> RuleDecision
    abstract member ValidatePermissionUpdate: PermissionUpdateRuleContext -> RuleDecision


type MessageDomainRules() =
    let allow () =
        RuleDecision(IsAllowed = true, ErrorMessage = null)

    let deny message =
        RuleDecision(IsAllowed = false, ErrorMessage = message)

    let normalizeScope (scope: string) =
        if String.IsNullOrWhiteSpace(scope) then ""
        else scope.Trim().ToLowerInvariant()

    let isSupportedScopeForMessage scope =
        scope = "channel" || scope = "group" || scope = "private"

    let isSupportedScopeForRoleAndPermission scope =
        scope = "channel" || scope = "group"

    interface IMessageDomainRules with
        member _.ValidateMessageSend(context: MessageRuleContext) =
            let scope = normalizeScope context.Scope
            let normalizedText = if isNull context.Content then String.Empty else context.Content.Trim()

            if not (isSupportedScopeForMessage scope) then
                deny "Invalid message scope"
            elif context.SenderUserId = 0UL then
                deny "Invalid sender"
            elif context.TargetId = 0UL then
                deny "Invalid target"
            elif context.AttachmentCount < 0 then
                deny "Invalid attachments count"
            elif context.AttachmentCount > 10 then
                deny "Maximum 10 attachments are allowed per message"
            elif String.IsNullOrWhiteSpace(normalizedText) && context.AttachmentCount = 0 then
                deny "Message must contain text or at least one attachment"
            elif normalizedText.Length > 4000 then
                deny "Message text is too long"
            else
                allow ()

        member _.ValidateRoleUpdate(context: RoleUpdateRuleContext) =
            let scope = normalizeScope context.Scope

            if not (isSupportedScopeForRoleAndPermission scope) then
                deny "Invalid scope"
            elif context.TargetId = 0UL then
                deny "Invalid target"
            elif context.ActorUserId = 0UL then
                deny "Invalid actor"
            elif context.TargetUserId = 0UL then
                deny "Invalid target user"
            elif context.ActorUserId = context.TargetUserId then
                deny "Cannot update your own role"
            elif context.NewRole < 0 || context.NewRole > 3 then
                deny "Invalid role value"
            else
                allow ()

        member _.ValidatePermissionUpdate(context: PermissionUpdateRuleContext) =
            let scope = normalizeScope context.Scope

            if not (isSupportedScopeForRoleAndPermission scope) then
                deny "Invalid scope"
            elif context.TargetId = 0UL then
                deny "Invalid target"
            elif context.ActorUserId = 0UL then
                deny "Invalid actor"
            elif context.TargetUserId = 0UL then
                deny "Invalid target user"
            elif context.ActorUserId = context.TargetUserId then
                deny "Cannot update your own permissions"
            else
                allow ()
