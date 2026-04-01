namespace Aegis.DomainRules

open System

type FrameValidationError =
    | MessageTooShort of actualLength:int * minLength:int
    | PayloadTooLarge of payloadLength:uint32 * maxPayload:uint32
    | InvalidFrameSize of expected:int * actual:int

type InboundMessageKind =
    | Handshake
    | Auth
    | Ping
    | Message
    | Ack
    | Nack
    | RetransmitRequest
    | Register
    | UserPresence
    | UserSearch
    | ChannelMessage
    | ChannelCreate
    | ChannelJoin
    | PrivateChatMessage
    | ChatListRequest
    | PrivateChatHistoryRequest
    | ChannelHistoryRequest
    | ProfileUpdate
    | ProfileGet
    | ProfileAvatarAdd
    | ProfileAvatarList
    | ProfileAvatarDelete
    | ProfileAvatarSetPrimary
    | ChannelLinkUpdate
    | ChannelLinkGet
    | ChannelResolve
    | ChannelJoinByLink
    | MessageEdit
    | MessageDelete
    | ChannelEdit
    | GroupCreate
    | GroupEdit
    | GroupMessageSend
    | MemberRoleUpdate
    | MemberPermissionUpdate
    | GroupHistoryRequest
    | ChannelMembersRequest
    | GroupMembersRequest
    | ChannelLeave
    | GroupLeave
    | MessageReact
    | MessagePin
    | RoomSettingsGet
    | RoomSettingsUpdate

[<RequireQualifiedAccess>]
module ProtocolSafety =
    let validateFrameEnvelope (frameLength: int) (payloadLength: uint32) (headerSize: int) (macSize: int) (maxPayload: uint32) =
        if frameLength < headerSize then
            Error (MessageTooShort (frameLength, headerSize))
        elif payloadLength > maxPayload then
            Error (PayloadTooLarge (payloadLength, maxPayload))
        else
            let expectedSize = headerSize + int payloadLength + macSize
            if frameLength <> expectedSize then
                Error (InvalidFrameSize (expectedSize, frameLength))
            else
                Ok expectedSize

    let formatFrameError = function
        | MessageTooShort (actual, minLen) -> $"Message too short: {actual}, min {minLen}"
        | PayloadTooLarge (payload, maxPayload) -> $"Payload too large: {payload}, max {maxPayload}"
        | InvalidFrameSize (expected, actual) -> $"Invalid frame size: expected {expected}, got {actual}"

    let tryClassifyInboundMessageType (messageTypeValue: uint16) =
        match int messageTypeValue with
        | 6 -> Some InboundMessageKind.Handshake
        | 1 -> Some InboundMessageKind.Auth
        | 2 -> Some InboundMessageKind.Ping
        | 3 -> Some InboundMessageKind.Message
        | 4 -> Some InboundMessageKind.Ack
        | 7 -> Some InboundMessageKind.Nack
        | 8 -> Some InboundMessageKind.RetransmitRequest
        | 20 -> Some InboundMessageKind.Register
        | 9 -> Some InboundMessageKind.UserPresence
        | 18 -> Some InboundMessageKind.UserSearch
        | 13 -> Some InboundMessageKind.ChannelMessage
        | 14 -> Some InboundMessageKind.ChannelCreate
        | 15 -> Some InboundMessageKind.ChannelJoin
        | 17 -> Some InboundMessageKind.PrivateChatMessage
        | 41 -> Some InboundMessageKind.ChatListRequest
        | 43 -> Some InboundMessageKind.PrivateChatHistoryRequest
        | 45 -> Some InboundMessageKind.ChannelHistoryRequest
        | 22 -> Some InboundMessageKind.ProfileUpdate
        | 24 -> Some InboundMessageKind.ProfileGet
        | 49 -> Some InboundMessageKind.ProfileAvatarAdd
        | 51 -> Some InboundMessageKind.ProfileAvatarList
        | 53 -> Some InboundMessageKind.ProfileAvatarDelete
        | 55 -> Some InboundMessageKind.ProfileAvatarSetPrimary
        | 57 -> Some InboundMessageKind.ChannelLinkUpdate
        | 59 -> Some InboundMessageKind.ChannelLinkGet
        | 61 -> Some InboundMessageKind.ChannelResolve
        | 63 -> Some InboundMessageKind.ChannelJoinByLink
        | 26 -> Some InboundMessageKind.MessageEdit
        | 28 -> Some InboundMessageKind.MessageDelete
        | 30 -> Some InboundMessageKind.ChannelEdit
        | 11 -> Some InboundMessageKind.GroupCreate
        | 32 -> Some InboundMessageKind.GroupEdit
        | 38 -> Some InboundMessageKind.GroupMessageSend
        | 34 -> Some InboundMessageKind.MemberRoleUpdate
        | 36 -> Some InboundMessageKind.MemberPermissionUpdate
        | 70 -> Some InboundMessageKind.GroupHistoryRequest
        | 73 -> Some InboundMessageKind.ChannelMembersRequest
        | 75 -> Some InboundMessageKind.GroupMembersRequest
        | 16 -> Some InboundMessageKind.ChannelLeave
        | 12 -> Some InboundMessageKind.GroupLeave
        | 77 -> Some InboundMessageKind.MessageReact
        | 80 -> Some InboundMessageKind.MessagePin
        | 83 -> Some InboundMessageKind.RoomSettingsGet
        | 85 -> Some InboundMessageKind.RoomSettingsUpdate
        | _ -> None

type ProtocolSafetyFacade private () =
    static member ValidateFrameEnvelope(frameLength: int, payloadLength: uint32, headerSize: int, macSize: int, maxPayload: uint32) : string =
        match ProtocolSafety.validateFrameEnvelope frameLength payloadLength headerSize macSize maxPayload with
        | Ok _ -> null
        | Error err -> ProtocolSafety.formatFrameError err

    static member IsRoutableInboundType(messageTypeValue: uint16) : bool =
        ProtocolSafety.tryClassifyInboundMessageType messageTypeValue |> Option.isSome
