# Aegis Client Protocol Guide

This document is for client and SDK authors. It describes how to connect to an Aegis server, how frames and payloads are encoded, which request and event types matter for a real client app, and which implementation details are important for compatibility.

This is a practical guide, not a complete server-internals reference. The protocol source of truth is still:

- `src/Aegis.Protocol/MessageType.cs`
- `src/Aegis.Protocol/MessageEncoder.cs`
- `src/Aegis.Server/Program.cs`
- `src/Aegis.Server/ServerMessageSender.cs`
- `src/Aegis.Handlers/*.cs`

## 1. Protocol Overview

Aegis is a persistent binary protocol over TCP.

- Transport: TCP
- Optional transport TLS: server-configurable
- Optional transport masking: server-configurable
- Application payload format: MessagePack with string keys
- Post-handshake payload encryption: AES-256-GCM
- Session key establishment: ECDH P-256 + HKDF-SHA256

Typical client flow:

1. Open TCP connection.
2. Send `Handshake (6)`.
3. Receive `Handshake (6)` response with server public key.
4. Derive session key.
5. Send `Auth (1)` with username/password or session token.
6. Load bootstrap data with `ChatListRequest (41)` and history requests.
7. Keep the socket open and process pushed realtime events.
8. Send delivery/read receipts for displayed messages.

Important exception:

- `Register (20)` is allowed before handshake/auth.
- All other app-level messages are rejected before handshake.

## 2. Frame Format

Every frame starts with a fixed 21-byte header, encoded in big-endian order.

| Field | Size | Notes |
| --- | ---: | --- |
| `Magic` | 4 bytes | Constant `0x0AE6C5D7` |
| `VersionMajor` | 1 byte | Currently `1` |
| `VersionMinor` | 1 byte | Currently `0` |
| `Flags` | 1 byte | Compression/encryption/ack flags |
| `Type` | 2 bytes | `ushort`, see message catalog |
| `SequenceId` | 8 bytes | Client-defined monotonic sequence |
| `PayloadLength` | 4 bytes | Number of payload bytes after the header |

Then comes `PayloadLength` bytes of payload.

Server limits:

- Maximum total frame size: 1 MiB
- Maximum payload size: `1 MiB - 21 bytes`

## 3. Flags

Known `Flags` bits:

| Bit | Name | Meaning |
| --- | --- | --- |
| `0x01` | `RequiresAck` | Transport-level ACK requested |
| `0x02` | `IsRetransmit` | This frame is a retransmission |
| `0x04` | `Compressed` | Payload is Brotli-compressed |
| `0x08` | `Encrypted` | Payload is AES-GCM encrypted |
| `0x10` | `Priority` | High-priority delivery |

For normal client implementations, the important bits are `Compressed` and `Encrypted`.

## 4. Payload Encoding Rules

Application payloads are MessagePack-encoded objects using contractless string-key serialization.

That means:

- Keys are field/property names such as `Username`, `ChannelId`, `MessageIds`.
- The wire format is binary MessagePack, not JSON.
- Examples in this document are shown as JSON-like maps only for readability.

Recommended client behavior:

- Encode outgoing payloads as MessagePack maps with string keys.
- Decode incoming payloads as MessagePack first.
- Be tolerant when parsing timestamps.

Timestamp compatibility matters in practice:

- Current server code writes `DateTime` values through MessagePack.
- Some clients may decode them as native timestamp objects.
- Older or compatibility paths may expose them as ISO-8601 strings.

If you are writing a client library, parse timestamps defensively.

## 5. Compression and Encryption

### 5.1 Compression

Server behavior:

- Raw payloads larger than 512 bytes may be Brotli-compressed.
- Compression is only kept if it actually reduces size.

Client receive pipeline:

1. Read header.
2. If `Encrypted` is set, decrypt first.
3. If `Compressed` is set, Brotli-decompress next.
4. Decode MessagePack payload.

### 5.2 Encryption

After handshake, payload encryption may be required by server policy.

Incoming encrypted payload layout is:

- 12-byte nonce
- ciphertext
- 16-byte GCM tag appended to ciphertext bytes

Additional authenticated data for AES-GCM is the 21-byte frame header as transmitted on the wire.

Important ordering:

- Sender compresses first, then encrypts.
- Receiver decrypts first, then decompresses.

## 6. Handshake

Message type: `Handshake (6)`

Request payload:

```json
{
  "PublicKey": "<base64-encoded client ECDH public key>",
  "ClientVersion": 1,
  "AppId": 1001,
  "AppHash": "optional app credential"
}
```

Response payload:

```json
{
  "Success": true,
  "ServerPublicKey": "<base64>",
  "Message": "Handshake established",
  "Signature": "<optional base64 signature>",
  "SignatureAlgorithm": "ECDSA_P256_SHA256"
}
```

Notes:

- `AppId` and `AppHash` may be mandatory depending on server config.
- The response uses the same message type `Handshake (6)`, not a separate `HandshakeResponse` enum.
- If the server is configured to require signed handshake responses, validate the signature before trusting the key exchange.
- The derived session key is 32 bytes.

## 7. Authentication

Message type: `Auth (1)`

Request payload:

```json
{
  "Username": "alice",
  "Password": "secret",
  "Token": "",
  "ClientInfo": "my-client/1.0"
}
```

Token re-authentication uses the same payload and sets `Token` instead of `Username`/`Password`.

Response payload:

```json
{
  "Success": true,
  "UserId": 42,
  "Username": "alice",
  "SessionToken": "<server-issued token>",
  "Error": ""
}
```

Notes:

- Do not send regular app traffic before successful auth.
- After successful auth, the server may immediately push undelivered private messages as `PrivateChatMessageEvent (47)` frames.

## 8. Registration

Message type: `Register (20)`

Request payload:

```json
{
  "Username": "alice",
  "Email": "alice@example.com",
  "Password": "secret",
  "PublicKey": "<application-level public key>"
}
```

Compatibility fields accepted by the server:

- `Mail` instead of `Email`
- `PublicKeyLegacy` instead of `PublicKey`

Response type: `RegisterResponse (21)`

```json
{
  "Success": true,
  "Message": null,
  "User": {
    "Id": 42,
    "Username": "alice"
  }
}
```

## 9. Sequencing, Responses, and Events

General rule:

- Request/response pairs usually reuse the request `SequenceId`.
- Server-pushed events usually use `SequenceId = 0`.

Important exception:

- Read and delivery receipt confirmations currently use `SequenceId = 0` as well.

Current implementation detail:

- Some handlers return a response payload on the same `MessageType` as the request instead of using a distinct `...Response` enum value.
- This currently applies to `PrivateChatMessage (17)`, `ChannelMessage (13)`, `ChannelCreate (14)`, `ChannelJoin (15)`, `ChannelLeave (16)`, and `GroupLeave (12)`.

So client libraries should not assume that every logical response can be matched only by sequence. For receipt confirmations and pushed events, matching by message type is safer.

Recommended client strategy:

- Use monotonically increasing `SequenceId` for client-originated requests.
- Match normal request/response exchanges by `SequenceId` and expected response type.
- Route `SequenceId = 0` frames through your event/async pipeline.

## 10. Bootstrap Flow for a Real Client App

After auth, a typical UI client should do this:

1. Send `ChatListRequest (41)`.
2. Render direct chats, channels, and groups from `ChatListResponse (42)`.
3. For the currently opened conversation, load history with one of:
   - `PrivateChatHistoryRequest (43)`
   - `ChannelHistoryRequest (45)`
   - `GroupHistoryRequest (70)`
4. Subscribe your runtime router to pushed events:
   - `PrivateChatMessageEvent (47)`
   - `ChannelMessageEvent (48)`
   - `GroupMessageEvent (72)`
   - `MessageStatusEvent (69)`
   - `MessageReactionEvent (79)`
   - `MessagePinEvent (82)`
5. Send delivery and read receipts as messages become visible/read.

## 11. Core Chat APIs

### 11.1 Chat list

Request type: `ChatListRequest (41)`

Payload:

```json
{}
```

Response type: `ChatListResponse (42)`

```json
{
  "Success": true,
  "Chats": [
    {
      "ChatId": 100,
      "Type": "direct",
      "Title": "bob",
      "AvatarUrl": null,
      "PresenceStatus": "online",
      "LastMessage": "hi",
      "LastMessageAt": "timestamp",
      "UnreadCount": 2,
      "PeerUserId": 77,
      "ChannelId": null
    }
  ],
  "Message": null
}
```

`Type` values currently used by the server:

- `direct`
- `channel`
- `group`

### 11.2 Direct message send

Request type: `PrivateChatMessage (17)`

```json
{
  "ToUserId": 77,
  "Content": "hello",
  "ContentType": 0,
  "Attachment": null,
  "Attachments": null,
  "ParseMode": null
}
```

Response payload arrives on the same frame type: `PrivateChatMessage (17)`

```json
{
  "Success": true,
  "MessageId": 9001,
  "MessageText": "Message sent"
}
```

Practical note:

- Realtime inbound direct messages arrive as `PrivateChatMessageEvent (47)`.

Realtime event payload:

```json
{
  "Id": 9001,
  "FromUserId": 77,
  "ToUserId": 42,
  "Content": "hello",
  "ContentType": 0,
  "CreatedAt": "timestamp",
  "DeliveredTo": [42],
  "ReadBy": [],
  "FromUsername": "bob",
  "Username": "bob"
}
```

### 11.3 Direct history

Request type: `PrivateChatHistoryRequest (43)`

```json
{
  "PeerUserId": 77,
  "Limit": 100,
  "BeforeMessageId": null
}
```

Response type: `PrivateChatHistoryResponse (44)`

```json
{
  "Success": true,
  "PeerUserId": 77,
  "Messages": [
    {
      "Id": 9001,
      "FromUserId": 77,
      "ToUserId": 42,
      "Content": "hello",
      "ContentType": 0,
      "CreatedAt": "timestamp",
      "DeliveredTo": [42],
      "ReadBy": [],
      "FromUsername": "bob",
      "Username": "bob"
    }
  ],
  "Message": null
}
```

### 11.4 Channel message send

Request type: `ChannelMessage (13)`

```json
{
  "ChannelId": 123,
  "Content": "hello channel",
  "ContentType": 0,
  "ReplyToMessageId": null,
  "Attachment": null,
  "Attachments": null,
  "ParseMode": null
}
```

Response payload arrives on the same frame type: `ChannelMessage (13)`

```json
{
  "Success": true,
  "MessageId": 5001,
  "MessageText": "Message sent"
}
```

Realtime event type: `ChannelMessageEvent (48)`

```json
{
  "Id": 5001,
  "ChannelId": 123,
  "FromUserId": 42,
  "Content": "hello channel",
  "ContentType": 0,
  "CreatedAt": "timestamp",
  "DeliveredTo": [42],
  "ReadBy": [],
  "FromUsername": "alice",
  "ChannelName": "news"
}
```

### 11.5 Channel history

Request type: `ChannelHistoryRequest (45)`

```json
{
  "ChannelId": 123,
  "Limit": 100,
  "BeforeMessageId": null
}
```

Response type: `ChannelHistoryResponse (46)`

```json
{
  "Success": true,
  "ChannelId": 123,
  "ChannelName": "news",
  "Messages": [
    {
      "Id": 5001,
      "ChannelId": 123,
      "FromUserId": 42,
      "Content": "hello channel",
      "ContentType": 0,
      "CreatedAt": "timestamp",
      "DeliveredTo": [42],
      "ReadBy": [],
      "FromUsername": "alice",
      "ChannelName": "news"
    }
  ],
  "Message": null
}
```

### 11.6 Group message send and history

Send request type: `GroupMessageSend (38)`

```json
{
  "GroupId": 321,
  "Content": "hello group",
  "ContentType": 0,
  "ReplyToMessageId": null,
  "Attachment": null,
  "Attachments": null,
  "ParseMode": null
}
```

Send response type: `GroupMessageResponse (39)`

```json
{
  "Success": true,
  "MessageId": 7001,
  "Message": "Message sent"
}
```

History request type: `GroupHistoryRequest (70)`

```json
{
  "GroupId": 321,
  "Limit": 100,
  "BeforeMessageId": null
}
```

History response type: `GroupHistoryResponse (71)`

```json
{
  "Success": true,
  "GroupId": 321,
  "GroupName": "team",
  "Messages": [
    {
      "Id": 7001,
      "GroupId": 321,
      "FromUserId": 42,
      "Content": "hello group",
      "ContentType": 0,
      "CreatedAt": "timestamp",
      "DeliveredTo": [42],
      "ReadBy": [],
      "IsPinned": false,
      "FromUsername": "alice",
      "GroupName": "team"
    }
  ],
  "Message": null
}
```

Realtime event type: `GroupMessageEvent (72)`

```json
{
  "Id": 7001,
  "GroupId": 321,
  "FromUserId": 42,
  "Content": "hello group",
  "ContentType": 0,
  "CreatedAt": "timestamp",
  "FromUsername": "alice",
  "GroupName": "team"
}
```

Important note:

- `GroupMessageEvent` currently does not include `DeliveredTo` and `ReadBy`, unlike direct/channel message events.

## 12. Receipts and Status Updates

### 12.1 Delivery receipt

Request type: `MessageDeliveryReceipt (67)`

```json
{
  "MessageIds": [9001, 9002],
  "DeliveredAt": "timestamp",
  "DeviceId": "phone-1"
}
```

Confirmation type: `MessageDeliveryReceiptResponse (68)`

```json
{
  "Success": true,
  "MessageIds": [9001, 9002],
  "ProcessedAt": "timestamp"
}
```

### 12.2 Read receipt

Request type: `MessageReadReceipt (65)`

```json
{
  "MessageIds": [9001, 9002],
  "ReadAt": "timestamp"
}
```

Confirmation type: `MessageReadReceiptResponse (66)`

```json
{
  "Success": true,
  "MessageIds": [9001, 9002],
  "ProcessedAt": "timestamp"
}
```

### 12.3 Status event

Server-pushed type: `MessageStatusEvent (69)`

```json
{
  "Success": true,
  "MessageIds": [9001],
  "DeliveredTo": 42,
  "ReadBy": null,
  "ProcessedAt": "timestamp"
}
```

or:

```json
{
  "Success": true,
  "MessageIds": [9001],
  "DeliveredTo": null,
  "ReadBy": 42,
  "ProcessedAt": "timestamp"
}
```

## 13. Presence

Request type: `UserPresence (9)`

Practical payload:

```json
{
  "IsOnline": true,
  "ClientTimestamp": "2026-03-30T12:34:56Z"
}
```

Compatibility note:

- Server code accepts a normal typed timestamp.
- For broad client compatibility, ISO-8601 UTC strings are a safe choice.

Presence values returned elsewhere are string statuses such as:

- `online`
- `recently`
- `last_week`
- `long_ago`

Exact status mapping is a server concern. Client libraries should treat presence as display metadata, not as a hard state machine.

## 14. User and Profile APIs

### 14.1 User search

Request type: `UserSearch (18)`

```json
{
  "Query": "ali",
  "Limit": 20
}
```

Response payload:

```json
{
  "Success": true,
  "Users": [
    {
      "Id": 42,
      "Username": "alice",
      "PresenceStatus": "online"
    }
  ],
  "Message": null
}
```

### 14.2 Profile get

Request type: `ProfileGet (24)`

```json
{
  "UserId": 42,
  "Username": null
}
```

If both fields are omitted, the server returns the authenticated user's own profile.

Response type: `ProfileGetResponse (25)`

```json
{
  "Success": true,
  "Profile": {
    "Id": 42,
    "Username": "alice",
    "DisplayName": "Alice",
    "AvatarUrl": null,
    "Avatars": [],
    "PresenceStatus": "online",
    "Bio": null,
    "Location": null,
    "BirthDate": null,
    "Email": "alice@example.com",
    "CreatedAt": "timestamp",
    "LastSeenAt": "timestamp"
  },
  "Message": null
}
```

### 14.3 Profile update

Request type: `ProfileUpdate (22)`

```json
{
  "DisplayName": "Alice",
  "AvatarUrl": null,
  "Bio": "hello",
  "Username": null,
  "Location": null,
  "BirthDate": null
}
```

Response type: `ProfileUpdateResponse (23)`

- Same `ProfileData` shape as `ProfileGetResponse`.

### 14.4 Profile avatars

Supported types:

- `ProfileAvatarAdd (49)` -> `ProfileAvatarAddResponse (50)`
- `ProfileAvatarList (51)` -> `ProfileAvatarListResponse (52)`
- `ProfileAvatarDelete (53)` -> `ProfileAvatarDeleteResponse (54)`
- `ProfileAvatarSetPrimary (55)` -> `ProfileAvatarSetPrimaryResponse (56)`

Avatar item shape:

```json
{
  "Id": 1,
  "AvatarUrl": "https://...",
  "IsPrimary": true,
  "CreatedAt": "timestamp"
}
```

## 15. Channels, Groups, Membership, and Links

### 15.1 Channel create / join / leave / edit

Supported types:

- `ChannelCreate (14)` -> response payload currently arrives on `ChannelCreate (14)`
- `ChannelJoin (15)` -> response payload currently arrives on `ChannelJoin (15)`
- `ChannelLeave (16)` -> response payload currently arrives on `ChannelLeave (16)`
- `ChannelEdit (30)` -> `ChannelEditResponse (31)`

Common `ChannelSummary` shape:

```json
{
  "Id": 123,
  "Name": "news",
  "Description": "updates",
  "Type": 0,
  "MemberCount": 42
}
```

### 15.2 Group create / edit / leave

Supported types:

- `GroupCreate (11)` -> `GroupCreateResponse (40)`
- `GroupEdit (32)` -> `GroupEditResponse (33)`
- `GroupLeave (12)` -> response payload currently arrives on `GroupLeave (12)`

### 15.3 Member lists

Request types:

- `ChannelMembersRequest (73)`
- `GroupMembersRequest (75)`

Response item shape:

```json
{
  "UserId": 42,
  "Username": "alice",
  "Role": "owner",
  "JoinedAt": "timestamp",
  "CanSendMessages": true,
  "CanDeleteOthersMessages": true,
  "CanPinMessages": true,
  "CanManageRoles": true
}
```

### 15.4 Roles and permissions

Supported types:

- `MemberRoleUpdate (34)` -> `MemberRoleUpdateResponse (35)`
- `MemberPermissionUpdate (36)` -> `MemberPermissionUpdateResponse (37)`

Role update request:

```json
{
  "Scope": "channel",
  "TargetId": 123,
  "TargetUserId": 42,
  "NewRole": 2
}
```

Permission update request:

```json
{
  "Scope": "group",
  "TargetId": 321,
  "TargetUserId": 77,
  "CanSendMessages": true,
  "CanDeleteOthersMessages": null,
  "CanEditInfo": null,
  "CanInviteUsers": null,
  "CanRemoveUsers": null,
  "CanPinMessages": true,
  "CanManageRoles": false
}
```

### 15.5 Channel links

Supported types:

- `ChannelLinkUpdate (57)` -> `ChannelLinkUpdateResponse (58)`
- `ChannelLinkGet (59)` -> `ChannelLinkGetResponse (60)`
- `ChannelResolve (61)` -> `ChannelResolveResponse (62)`
- `ChannelJoinByLink (63)` -> `ChannelJoinByLinkResponse (64)`

`ChannelLinkInfo` shape:

```json
{
  "ChannelId": 123,
  "PublicAlias": "news",
  "PublicLink": "aegis://channel/news",
  "PrivateInviteLink": "aegis://invite/abcdef"
}
```

`ChannelJoinByLink` uses the same request shape as `ChannelResolve`:

```json
{
  "LinkOrAlias": "news"
}
```

## 16. Message Mutation, Reactions, Pins, and Room Settings

### 16.1 Edit and delete

Supported types:

- `MessageEdit (26)` -> `MessageEditResponse (27)`
- `MessageDelete (28)` -> `MessageDeleteResponse (29)`

Edit request:

```json
{
  "MessageId": 9001,
  "NewContent": "edited text",
  "Scope": "private",
  "ChannelId": null,
  "GroupId": null
}
```

Delete request:

```json
{
  "MessageId": 9001,
  "Scope": "channel",
  "ChannelId": 123,
  "GroupId": null
}
```

### 16.2 Reactions

Request type: `MessageReact (77)`

```json
{
  "Scope": "channel",
  "MessageId": 5001,
  "Emoji": "🔥",
  "Remove": false
}
```

Response type: `MessageReactResponse (78)`

```json
{
  "Success": true,
  "Message": null,
  "Reactions": [
    {
      "Emoji": "🔥",
      "Count": 3,
      "ByMe": true
    }
  ]
}
```

Event type: `MessageReactionEvent (79)`

```json
{
  "Scope": "channel",
  "MessageId": 5001,
  "UserId": 42,
  "Emoji": "🔥",
  "Removed": false,
  "Reactions": [
    {
      "Emoji": "🔥",
      "Count": 3,
      "ByMe": true
    }
  ]
}
```

### 16.3 Pins

Request type: `MessagePin (80)`

```json
{
  "Scope": "group",
  "MessageId": 7001,
  "TargetId": 321,
  "Unpin": false
}
```

Event type: `MessagePinEvent (82)`

```json
{
  "Scope": "group",
  "MessageId": 7001,
  "TargetId": 321,
  "Pinned": true,
  "ActorUserId": 42
}
```

### 16.4 Room settings

Supported types:

- `RoomSettingsGet (83)` -> `RoomSettingsGetResponse (84)`
- `RoomSettingsUpdate (85)` -> `RoomSettingsUpdateResponse (86)`

Get request:

```json
{
  "Scope": "channel",
  "TargetId": 123
}
```

Get response:

```json
{
  "Success": true,
  "Scope": "channel",
  "TargetId": 123,
  "JoinRule": 0,
  "HistoryVisibility": 1,
  "Message": null
}
```

## 17. Transport-Level ACK/NACK

The protocol also defines:

- `Ack (4)`
- `Nack (7)`
- `RetransmitRequest (8)`

ACK payload format is compact and binary, not a MessagePack object:

- `SequenceId`: 8 bytes, big-endian
- `AckStatus`: 1 byte

`AckStatus` values:

- `0` = `Ok`
- `1` = `Error`
- `2` = `Retry`
- `3` = `NotImplemented`

Many client apps can ignore this layer initially unless they implement custom reliability semantics beyond the current high-level request/event flow.

## 18. Recommended Client Library Architecture

For a maintainable SDK, split responsibilities like this:

1. `Transport`
   - TCP socket
   - optional TLS
   - reconnect loop
2. `FrameCodec`
   - read/write 21-byte header
   - big-endian integer helpers
3. `SecurityLayer`
   - ECDH handshake
   - HKDF session key derivation
   - AES-GCM encrypt/decrypt
4. `PayloadCodec`
   - MessagePack encode/decode
   - tolerant timestamp parsing
5. `RequestManager`
   - sequence generation
   - pending request map for normal request/response pairs
6. `EventRouter`
   - server-pushed event dispatch
   - special handling for `SequenceId = 0`
7. `Feature APIs`
   - auth
   - chats/history
   - channels/groups
   - profile
   - receipts/reactions/pins

This separation makes it easier to support both a UI application and a reusable SDK.

## 19. Compatibility Guidance

If you want a client that works reliably with the current server code, keep these rules:

1. Treat payloads as MessagePack, not JSON.
2. Parse timestamps flexibly.
3. Expect async events to use `SequenceId = 0`.
4. Expect receipt confirmations to use `SequenceId = 0` too.
5. After auth, be ready for immediate pushed messages.
6. Handle `Compressed` and `Encrypted` independently and in the correct order.
7. Use PascalCase field names when serializing request objects.
8. For presence updates, ISO-8601 UTC strings are a safe `ClientTimestamp` representation.

## 20. Minimal End-to-End Example

Pseudo-flow for a client session:

1. Connect TCP.
2. Send `Handshake (6)` with client ECDH public key.
3. Receive server public key, optionally verify signature, derive session key.
4. Send encrypted `Auth (1)`.
5. Receive `AuthResponse`.
6. Start background frame reader.
7. Send `ChatListRequest (41)`.
8. When opening a direct chat, send `PrivateChatHistoryRequest (43)`.
9. When user sends a message, send `PrivateChatMessage (17)`.
10. When a `PrivateChatMessageEvent (47)` arrives and is rendered, send `MessageDeliveryReceipt (67)`.
11. When the user reads it, send `MessageReadReceipt (65)`.
12. Update UI when `MessageStatusEvent (69)` arrives.

## 21. Related Files

For more detail, see:

- `protocol-overview.md`
- `wire-spec.md`
- `AegisDartClient/README.md`
- `src/Aegis.Protocol/MessageType.cs`
