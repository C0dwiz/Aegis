# Aegis Dart Client Migration Guide

This guide helps migrate existing integrations to the current Aegis Dart client.

## Scope

This migration note focuses on:

- handshake behavior changes;
- direct-message payload compatibility updates;
- new typing APIs;
- new file transfer APIs.

## 1. Handshake: strict V2 by default

Current versions use V2 staged handshake by default.

### Before

```dart
await client.connect('host', 8888);
```

### Now

The same call remains valid, but now expects V2 stages on the server.

```dart
await client.connect('host', 8888);
```

### Temporary fallback for mixed environments

Use legacy fallback only while old servers are still present:

```dart
await client.connect(
  'host',
  8888,
  allowLegacyHandshakeFallback: true,
);
```

### Recommended production mode

Pin signed handshake validation:

```dart
await client.connect(
  'host',
  8888,
  requireSignedHandshake: true,
  trustedServerHandshakeSigningPublicKeyBase64: '<server-signing-public-key-base64>',
);
```

## 2. MessageType additions

The Dart enum now includes server message types:

- `userTyping(87)`
- `userTypingEvent(88)`
- `fileTransfer(89)`
- `fileTransferResponse(90)`
- `fileTransferChunk(91)`

If your app had custom switch/case mapping for numeric message IDs, update it accordingly.

## 3. Private message payload compatibility

Direct-message payload models now support additional fields and aliases:

- event id can come from `Id` or `MessageId`;
- timestamp can come from `CreatedAt` or `CreatedAtUtc`;
- optional `SignalV3` metadata is supported in request/response/event payloads.

No code changes are required if you already use SDK payload classes.

## 4. Typing indicators

### New send APIs

```dart
await client.sendPrivateTyping(12345, isTyping: true);
await client.sendChannelTyping(1001, isTyping: true);
await client.sendGroupTyping(2001, isTyping: false);
```

or via facades:

```dart
await client.direct.sendTyping(12345, isTyping: true);
await client.channels.sendTyping(1001, isTyping: true);
await client.groups.sendTyping(2001, isTyping: false);
```

### New incoming event stream

```dart
client.typingEvents.listen((event) {
  // event.scope, event.targetId, event.userId, event.isTyping
});
```

## 5. File transfer APIs

### Upload flow

```dart
final init = await client.initializeFileUpload(
  fileName: 'doc.pdf',
  mimeType: 'application/pdf',
  totalSize: bytes.length,
  totalChunks: totalChunks,
  allowedUserIds: [12345],
);

await client.uploadFileChunk(
  transferId: init.transferId!,
  chunkIndex: 0,
  chunkBytes: chunk0,
);

await client.completeFileUpload(init.transferId!);
```

### Download flow

```dart
final start = await client.startFileDownload(fileId);
final data = await client.downloadFileBytes(fileId);
```

### Chunk stream

```dart
client.fileTransferChunkEvents.listen((chunk) {
  // chunk.fileId, chunk.chunkIndex, chunk.chunkDataBase64
});
```

## 6. Direct message API with SignalV3 envelope

Optional SignalV3 envelope can be passed when sending private messages:

```dart
await client.sendPrivateMessage(
  12345,
  'encrypted payload placeholder',
  signalV3: SignalV3EnvelopePayload(
    ciphertextBase64: '<base64-ciphertext>',
    messageNumber: 42,
  ),
);
```

Facade path also supports this:

```dart
await client.direct.sendMessage(
  12345,
  'encrypted payload placeholder',
  signalV3: SignalV3EnvelopePayload(
    ciphertextBase64: '<base64-ciphertext>',
    messageNumber: 42,
  ),
);
```

## 7. Recommended upgrade checklist

1. Update package version and run tests.
2. Validate server supports V2 handshake.
3. Enable `allowLegacyHandshakeFallback` only if required during migration.
4. If you use signed handshake, configure trusted public key.
5. Adopt typing and file transfer APIs where needed.
6. Verify direct-message consumers handle optional `SignalV3` metadata.

## 8. Validation

A full Dart test run should remain green after migration:

```bash
dart test
```
