# Aegis Dart Client V2 Usage

This guide shows how to use the Dart client with the staged V2 handshake in real code.

## 1. Install and import

```yaml
dependencies:
  aegis_client: ^1.0.0
```

```dart
import 'package:aegis_client/aegis_client.dart';
```

## 2. Create client credentials

Use issued `api_id` / `api_hash` from your portal app:

```dart
final client = AegisClient.withApiCredentials(
  const AegisApiCredentials(
    appId: 50001,
    appHash: 'issued-app-hash',
  ),
);
```

You can also use built-in first-party credentials:

```dart
final client = AegisClient.official();
```

## 3. Connect with strict V2 (default)

`allowLegacyHandshakeFallback` is `false` by default, so client expects V2 handshake stages.

```dart
await client.connect(
  'your-host',
  8888,
);
```

## 4. Enable signed handshake validation (recommended)

```dart
await client.connect(
  'your-host',
  8888,
  requireSignedHandshake: true,
  trustedServerHandshakeSigningPublicKeyBase64: '<server-signing-public-key-base64>',
);
```

## 5. Temporary migration mode (legacy fallback)

Use this only while old servers are still present.

```dart
await client.connect(
  'your-host',
  8888,
  allowLegacyHandshakeFallback: true,
);
```

## 6. Authenticate and use APIs

```dart
await client.login('alice', 'password123');

final chats = await client.getChatList();
print('Chats: ${chats.chats.length}');

await client.sendPrivateMessage(12345, 'Hello from V2 client');
await client.ping();
```

## 7. Safe shutdown

```dart
await client.disconnect();
client.dispose();
```

## Notes

- V2 handshake flow is `client_hello_v2 -> server_hello_v2 -> client_finish_v2 -> server_finish_v2`.
- Session keys are derived with HKDF using handshake transcript and nonces.
- `requireSignedHandshake` protects against spoofed handshake responses.
- For production, prefer strict V2 and keep legacy fallback disabled.
