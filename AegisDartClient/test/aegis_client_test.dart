import 'dart:typed_data';
import 'package:msgpack_dart/msgpack_dart.dart' as msgpack;
import 'package:test/test.dart';
import 'package:aegis_client/aegis_client.dart';

void main() {
  group('API credentials', () {
    test('should expose official credentials', () {
      expect(AegisOfficialApiCredentials.credentials.appId, equals(2041001));
      expect(
        AegisOfficialApiCredentials.credentials.appHash,
        equals(
            '8f4c1db0e7c2456d9ab31f4e6d8c9a0137f2c4b56d8e1a903bc7d52e6f194a3c'),
      );
    });

    test('should use official credentials by default', () {
      final client = AegisClient();

      expect(client.apiCredentials, isNotNull);
      expect(
        client.apiCredentials!.appId,
        equals(AegisOfficialApiCredentials.credentials.appId),
      );

      client.dispose();
    });

    test('should allow explicit custom credentials', () {
      final client = AegisClient.withApiCredentials(
        const AegisApiCredentials(appId: 99, appHash: 'custom-hash'),
      );

      expect(client.apiCredentials, isNotNull);
      expect(client.apiCredentials!.appId, equals(99));
      expect(client.apiCredentials!.appHash, equals('custom-hash'));

      client.dispose();
    });

    test('should allow disabling api credentials', () {
      final client = AegisClient.withoutApiCredentials();

      expect(client.apiCredentials, isNull);

      client.dispose();
    });
  });

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
      expect(
          decodedMessage.payloadLength, equals(originalMessage.payloadLength));
      expect(decodedMessage.payload, equals(originalMessage.payload));
    });

    test('should throw ProtocolError for invalid magic', () {
      final data =
          Uint8List.fromList([0x00, 0x00, 0x00, 0x00]); // Invalid magic

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

    test('should throw ProtocolError for trailing bytes', () {
      final originalMessage = Message.withType(
        MessageType.ping,
        Uint8List.fromList([1, 2, 3]),
      );
      originalMessage.sequenceId = 7;

      final encoded = MessageEncoder.encode(originalMessage);
      final withTail = Uint8List(encoded.length + 2)
        ..setRange(0, encoded.length, encoded)
        ..setRange(encoded.length, encoded.length + 2, [0xAA, 0xBB]);

      expect(
        () => MessageEncoder.decode(withTail),
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
      expect(ProtocolConstants.headerSize, equals(21));
      expect(ProtocolConstants.macSize, equals(0));
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

  group('MessagePack compatibility', () {
    test('should decode chat list response from MessagePack', () {
      final bytes = msgpack.serialize({
        'Success': true,
        'Chats': [
          {
            'ChatId': 42,
            'Type': 'channel',
            'Title': 'general',
            'LastMessage': 'hello',
            'LastMessageAt': '2026-04-01T09:45:00Z',
            'UnreadCount': 3,
            'ChannelId': 42,
          }
        ]
      });

      final response = ChatListResponse.fromBytes(bytes);

      expect(response.success, isTrue);
      expect(response.chats, hasLength(1));
      expect(response.chats.single.chatId, equals(42));
      expect(
        response.chats.single.lastMessageAt,
        equals(DateTime.utc(2026, 4, 1, 9, 45)),
      );
    });

    test('should accept DateTime values in decoded history maps', () {
      final response = ChannelHistoryResponse.fromJson({
        'Success': true,
        'ChannelId': 9,
        'Messages': [
          {
            'Id': 100,
            'ChannelId': 9,
            'FromUserId': 7,
            'Content': 'hello',
            'ContentType': 0,
            'CreatedAt': DateTime.utc(2026, 4, 1, 9, 46),
            'DeliveredTo': <int>[],
            'ReadBy': <int>[],
          }
        ]
      });

      expect(
        response.messages.single.createdAt,
        equals(DateTime.utc(2026, 4, 1, 9, 46)),
      );
    });
  });
}
