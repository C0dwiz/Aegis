/// Aegis Client Library for Dart
/// 
/// A complete Dart implementation of the Aegis Messenger Protocol client
/// for connecting to Aegis servers and sending/receiving messages.
library aegis_client;

// Core client
export 'src/aegis_client.dart';

// Protocol components
export 'src/message.dart';
export 'src/message_type.dart';
export 'src/message_payloads.dart';
export 'src/message_encoder.dart';
export 'src/protocol_constants.dart';

// Transport layer
export 'src/transport.dart';

// Exceptions
export 'src/exceptions.dart';

// Logging
export 'src/logger.dart';
