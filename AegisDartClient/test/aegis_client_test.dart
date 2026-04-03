import 'dart:typed_data';

import 'package:es_compression/brotli.dart';
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
      expect(MessageType.fromValue(88), equals(MessageType.userTypingEvent));
      expect(
          MessageType.fromValue(90), equals(MessageType.fileTransferResponse));
      expect(MessageType.fromValue(999), equals(MessageType.unknown));
    });
  });

  group('Ergonomic enums', () {
    test('should expose protocol-compatible scope values', () {
      expect(ChatScope.privateChat.value, equals('private'));
      expect(ChatScope.channel.value, equals('channel'));
      expect(ChatScope.group.value, equals('group'));
      expect(RoomScope.channel.value, equals('channel'));
      expect(RoomScope.group.value, equals('group'));
    });

    test('should expose protocol-compatible role and room setting values', () {
      expect(MemberRole.member.value, equals(0));
      expect(MemberRole.moderator.value, equals(1));
      expect(MemberRole.admin.value, equals(2));
      expect(MemberRole.owner.value, equals(3));
      expect(RoomJoinRule.open.value, equals(0));
      expect(RoomJoinRule.inviteOnly.value, equals(1));
      expect(RoomJoinRule.approval.value, equals(2));
      expect(RoomHistoryVisibility.worldReadable.value, equals(0));
      expect(RoomHistoryVisibility.joined.value, equals(1));
      expect(RoomHistoryVisibility.invited.value, equals(2));
    });

    test('should expose computed enum getters on room settings response', () {
      final response = RoomSettingsGetResponse.fromJson({
        'Success': true,
        'Scope': 'group',
        'TargetId': 55,
        'JoinRule': 2,
        'HistoryVisibility': 0,
      });

      expect(response.roomScope, equals(RoomScope.group));
      expect(response.joinRuleValue, equals(RoomJoinRule.approval));
      expect(
        response.historyVisibilityValue,
        equals(RoomHistoryVisibility.worldReadable),
      );
    });

    test('should provide typed payload wrappers for scopes', () {
      final edit = MessageEditRequest.channel(
        channelId: 9,
        messageId: 100,
        newContent: 'updated',
      );
      final delete = MessageDeleteRequest.group(groupId: 7, messageId: 200);
      final reaction = MessageReactRequest.privateChat(
        messageId: 300,
        emoji: '🔥',
      );
      final pin = MessagePinRequest.channel(channelId: 5, messageId: 400);

      expect(edit.chatScope, equals(ChatScope.channel));
      expect(edit.channelId, equals(9));
      expect(delete.chatScope, equals(ChatScope.group));
      expect(delete.groupId, equals(7));
      expect(reaction.chatScope, equals(ChatScope.privateChat));
      expect(pin.roomScope, equals(RoomScope.channel));
      expect(pin.targetId, equals(5));
    });
  });

  group('Fluent facades', () {
    test('should expose fluent channel/group/direct facades', () {
      final client = AegisClient.withoutApiCredentials();

      expect(client.channels, isNotNull);
      expect(client.groups, isNotNull);
      expect(client.direct, isNotNull);

      client.dispose();
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

    test('should decode same-type channel join response from MessagePack', () {
      final bytes = msgpack.serialize({
        'Success': true,
        'Channel': {
          'Id': 77,
          'Name': 'general',
          'Description': 'main room',
          'Type': 0,
          'MemberCount': 10,
        },
        'Message': 'Joined channel',
      });

      final response = ChannelJoinResponse.fromBytes(bytes);

      expect(response.success, isTrue);
      expect(response.channel, isNotNull);
      expect(response.channel!.id, equals(77));
      expect(response.channel!.name, equals('general'));
    });

    test('should decode private send response from MessagePack', () {
      final bytes = msgpack.serialize({
        'Success': true,
        'MessageId': 9001,
        'MessageText': 'Message sent',
      });

      final response = PrivateChatMessageResponse.fromBytes(bytes);

      expect(response.success, isTrue);
      expect(response.messageId, equals(9001));
      expect(response.messageText, equals('Message sent'));
    });

    test('should decode private event with SignalV3 metadata and CreatedAtUtc',
        () {
      final bytes = msgpack.serialize({
        'MessageId': 7001,
        'FromUserId': 17,
        'ToUserId': 18,
        'Content': 'ciphertext',
        'ContentType': 0,
        'CreatedAtUtc': '2026-04-03T12:30:00Z',
        'SignalV3': {
          'MessageNumber': 5,
          'MessageKeyId': 'ABCD1234EFGH5678',
          'RatchetUpdatedAtUtc': '2026-04-03T12:30:00Z',
        }
      });

      final event = PrivateChatMessageEvent.fromBytes(bytes);

      expect(event.id, equals(7001));
      expect(event.signalV3, isNotNull);
      expect(event.signalV3!.messageNumber, equals(5));
      expect(event.signalV3!.messageKeyId, equals('ABCD1234EFGH5678'));
      expect(event.createdAt, equals(DateTime.utc(2026, 4, 3, 12, 30)));
    });

    test('should decode typing event payload from MessagePack', () {
      final bytes = msgpack.serialize({
        'Scope': 'private',
        'TargetId': 42,
        'UserId': 7,
        'IsTyping': true,
        'TimestampUtc': '2026-04-03T12:40:00Z',
      });

      final event = UserTypingEventPayload.fromBytes(bytes);
      expect(event.chatScope, equals(ChatScope.privateChat));
      expect(event.targetId, equals(42));
      expect(event.userId, equals(7));
      expect(event.isTyping, isTrue);
    });

    test('should decode file transfer response payload from MessagePack', () {
      final bytes = msgpack.serialize({
        'Success': true,
        'FileId': 'file_123',
        'TotalChunks': 3,
        'ChunkIndex': 1,
        'ChunkDataBase64': 'AQI=',
      });

      final response = FileTransferResponsePayload.fromBytes(bytes);
      expect(response.success, isTrue);
      expect(response.fileId, equals('file_123'));
      expect(response.totalChunks, equals(3));
      expect(response.chunkIndex, equals(1));
      expect(response.chunkDataBase64, equals('AQI='));
    });

    test('should decode profile avatar list from MessagePack', () {
      final bytes = msgpack.serialize({
        'Success': true,
        'Avatars': [
          {
            'Id': 1,
            'AvatarUrl': 'https://example/avatar.png',
            'IsPrimary': true,
            'CreatedAt': '2026-04-01T10:00:00Z',
          }
        ]
      });

      final response = ProfileAvatarListResponse.fromBytes(bytes);

      expect(response.success, isTrue);
      expect(response.avatars, hasLength(1));
      expect(response.avatars.single.isPrimary, isTrue);
      expect(
        response.avatars.single.createdAt,
        equals(DateTime.utc(2026, 4, 1, 10, 0)),
      );
    });

    test('should encode presence timestamp as ISO string for compatibility',
        () {
      final request = UserPresenceUpdateRequest(
        isOnline: true,
        clientTimestamp: DateTime.utc(2026, 4, 1, 10, 5),
      );

      final decoded =
          msgpack.deserialize(Uint8List.fromList(request.toBytes())) as Map;

      expect(decoded['IsOnline'], isTrue);
      expect(decoded['ClientTimestamp'], equals('2026-04-01T10:05:00.000Z'));
    });

    test('should decode receipt response from MessagePack', () {
      final bytes = msgpack.serialize({
        'Success': true,
        'MessageIds': [1, 2, 3],
        'ProcessedAt': '2026-04-01T10:10:00Z',
      });

      final response = MessageReceiptResponse.fromBytes(bytes);

      expect(response.success, isTrue);
      expect(response.messageIds, equals([1, 2, 3]));
      expect(
        response.processedAt,
        equals(DateTime.utc(2026, 4, 1, 10, 10)),
      );
    });
  });

  group('MessageEncoder compression behavior', () {
    test('should preserve pre-compressed payload without recompressing', () {
      final brotli = BrotliCodec();
      final rawPayload = Uint8List.fromList(List<int>.filled(2048, 1));
      final compressed = brotli.encode(rawPayload);
      final message = Message.withType(
        MessageType.message,
        compressed is Uint8List ? compressed : Uint8List.fromList(compressed),
      );
      message.sequenceId = 1;
      message.flags = ProtocolConstants.flagCompressed;

      final encoded = MessageEncoder.encode(message);
      final decoded = MessageEncoder.decode(encoded);

      expect(decoded.flags, equals(ProtocolConstants.flagNone));
      expect(decoded.payload, equals(rawPayload));
    });
  });
}
