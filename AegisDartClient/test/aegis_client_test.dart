import 'dart:typed_data';
import 'package:test/test.dart';
import 'package:aegis_client/aegis_client.dart';

void main() {
  group('MessageEncoder', () {
    test('should encode and decode message correctly', () {
      final originalMessage = Message.withType(
        MessageType.message,
        Uint8List.fromList('Hello World'.codeUnits),
      );
      originalMessage.sequenceId = 12345;
      originalMessage.flags = ProtocolConstants.flagRequiresAck;

      // Encode
      final encoded = MessageEncoder.encode(originalMessage);
      
      // Decode
      final decodedMessage = MessageEncoder.decode(encoded);

      expect(decodedMessage.magic, equals(originalMessage.magic));
      expect(decodedMessage.versionMajor, equals(originalMessage.versionMajor));
      expect(decodedMessage.versionMinor, equals(originalMessage.versionMinor));
      expect(decodedMessage.flags, equals(originalMessage.flags));
      expect(decodedMessage.type, equals(originalMessage.type));
      expect(decodedMessage.sequenceId, equals(originalMessage.sequenceId));
      expect(decodedMessage.payloadLength, equals(originalMessage.payloadLength));
      expect(decodedMessage.payload, equals(originalMessage.payload));
    });

    test('should throw ProtocolError for invalid magic', () {
      final data = Uint8List.fromList([0x00, 0x00, 0x00, 0x00]); // Invalid magic
      
      expect(
        () => MessageEncoder.decode(data),
        throwsA(isA<ProtocolError>()),
      );
    });

    test('should throw ProtocolError for too short message', () {
      final data = Uint8List(10); // Too short
      
      expect(
        () => MessageEncoder.decode(data),
        throwsA(isA<ProtocolError>()),
      );
    });
  });

  group('Message', () {
    test('should create valid message', () {
      final message = Message.withType(MessageType.ping);
      
      expect(message.isValid, isTrue);
      expect(message.type, equals(MessageType.ping));
      expect(message.magic, equals(ProtocolConstants.magic));
    });

    test('should calculate total size correctly', () {
      final payload = Uint8List.fromList('test'.codeUnits);
      final message = Message.withType(MessageType.message, payload);
      
      final expectedSize = ProtocolConstants.headerSize + 
                          payload.length + 
                          ProtocolConstants.macSize;
      
      expect(message.totalSize, equals(expectedSize));
    });
  });

  group('MessageType', () {
    test('should convert from value correctly', () {
      expect(MessageType.fromValue(1), equals(MessageType.auth));
      expect(MessageType.fromValue(2), equals(MessageType.ping));
      expect(MessageType.fromValue(3), equals(MessageType.message));
      expect(MessageType.fromValue(999), equals(MessageType.unknown));
    });
  });

  group('ProtocolConstants', () {
    test('should have correct values', () {
      expect(ProtocolConstants.magic, equals(0xAE6C5D7));
      expect(ProtocolConstants.versionMajor, equals(1));
      expect(ProtocolConstants.versionMinor, equals(0));
      expect(ProtocolConstants.headerSize, equals(20));
      expect(ProtocolConstants.macSize, equals(32));
    });
  });

  group('AegisLogger', () {
    test('should allow enabling/disabling', () {
      AegisLogger.enabled = true;
      expect(() => AegisLogger.info('test'), returnsNormally);
      
      AegisLogger.enabled = false;
      expect(() => AegisLogger.info('test'), returnsNormally);
    });

    test('should allow setting log level', () {
      AegisLogger.level = LogLevel.debug;
      expect(AegisLogger.level, equals(LogLevel.debug));
      
      AegisLogger.level = LogLevel.error;
      expect(AegisLogger.level, equals(LogLevel.error));
    });
  });
}
