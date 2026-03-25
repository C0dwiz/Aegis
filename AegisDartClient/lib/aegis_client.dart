/// Aegis Client Library for Dart
///
/// A complete Dart implementation of the Aegis Messenger Protocol client
/// for connecting to Aegis servers and sending/receiving messages.
///
/// ## Quick start
/// ```dart
/// import 'package:aegis_client/aegis_client.dart';
///
/// final client = AegisClient();
/// await client.connect('localhost', 5000);
/// await client.login('alice', 'password');
/// await client.sendPrivateMessage('bob', 'Hello!');
/// ```
///
/// ## Raw protocol access
/// For advanced users who want direct control over binary frames:
/// ```dart
/// final msg = Message.withType(MessageType.ping);
/// final bytes = MessageEncoder.encode(msg);
/// final decoded = MessageEncoder.decode(bytes);
/// ```
library aegis_client;

// Core client
export 'src/aegis_client.dart';

// Protocol components
export 'src/message.dart';
export 'src/message_type.dart';
export 'src/message_payloads.dart';
export 'src/message_encoder.dart';
export 'src/protocol_constants.dart';
export 'src/event_dispatcher.dart';

// Protocol internals (buffer management, checksums)
export 'src/buffer_pool.dart';
export 'src/ring_buffer.dart';
export 'src/crc32.dart';

// Transport layer
export 'src/transport.dart';

// Error types
export 'src/errors.dart';
export 'src/exceptions.dart';

// Security utilities
export 'src/security_utils.dart';

// Logging
export 'src/logger.dart';
