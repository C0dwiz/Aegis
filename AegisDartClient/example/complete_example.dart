import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'package:aegis_client/aegis_client.dart';

/// Complete protocol-oriented demo for the current server behavior.
Future<void> main() async {
  AegisLogger.enabled = true;
  AegisLogger.level = LogLevel.info;

  final client = AegisClient();
  final suffix = DateTime.now().millisecondsSinceEpoch;
  final username = 'dart_complete_$suffix';
  final password = 'test_password_123';
  StreamSubscription<PrivateChatMessageEvent>? privateSub;
  StreamSubscription<ChannelMessageEvent>? channelSub;

  try {
    print('Step 1: connect');
    await client.connect('localhost', 8888);

    print('Step 2: register');
    final reg = await client.register(
      username,
      'dart_complete_$suffix@example.com',
      password,
      'dart_public_key_placeholder',
    );
    print('Register success: ${reg.success}, userId: ${reg.user?.id ?? 0}');

    print('Step 3: authenticate');
    await client.authenticate(jsonEncode({
      'Username': username,
      'Password': password,
      'ClientInfo': 'aegis-dart-complete-example'
    }));
    print('Authenticated: ${client.isAuthenticated}');

    print('Step 3.1: subscribe to incoming events (unified attachment handler)');
    privateSub = client.onPrivateMessageEvent((event) {
      if (event.attachment != null) {
        _printAttachment(
          scope: 'private-event',
          contentType: event.contentType,
          text: event.content,
          attachment: event.attachment,
        );
      } else {
        print('[private-event] text=${event.content}');
      }
    });
    channelSub = client.onChannelMessageEvent((event) {
      if (event.attachment != null) {
        _printAttachment(
          scope: 'channel-event',
          contentType: event.contentType,
          text: event.content,
          attachment: event.attachment,
        );
      } else {
        print('[channel-event] text=${event.content}');
      }
    });

    print('Step 4: search users');
    final search = await client.searchUsers('dart_', limit: 10);
    print('Search success: ${search.success}');
    for (final u in search.users) {
      print('  user ${u.id}: ${u.username}');
    }

    print('Step 5: create public channel');
    final channel = await client.createChannel(
      'dart-complete-channel-$suffix',
      description: 'Channel from complete Dart example',
      type: ChannelType.public,
    );
    print('Channel success: ${channel.success}, channelId: ${channel.channelId}');

    print('Step 5.1: create group chat');
    final group = await client.createChannel(
      'dart-complete-group-$suffix',
      description: 'Group from complete Dart example',
      type: ChannelType.group,
    );
    print('Group success: ${group.success}, groupId: ${group.channelId}');

    if (channel.success && channel.channelId > 0) {
      print('Step 6: join channel');
      final join = await client.joinChannel(channel.channelId);
      print('Join success: ${join.success}, message: ${join.message ?? ''}');

      print('Step 7: send channel message');
      final channelMsg = await client.sendChannelMessage(
        channel.channelId,
        'hello from complete dart example',
      );
      print(
        'Channel message success: ${channelMsg.success}, '
        'messageId: ${channelMsg.messageId}, '
        'message: ${channelMsg.messageText ?? ''}',
      );

      final channelFileResp = await client.sendMedia(
        chatType: ChatTargetType.channel,
        chatId: channel.channelId,
        mediaBytes: Uint8List.fromList(utf8.encode('demo file bytes')),
        mediaKind: MediaKind.file,
        fileName: 'demo.txt',
        mimeType: 'text/plain',
        caption: 'file to channel',
      );
      print(
        'Channel file success: ${channelFileResp.success}, '
        'messageId: ${channelFileResp.messageId}',
      );
    }

    if (group.success && group.channelId > 0) {
      print('Step 7.1: send media to group via unified method');
      final groupMediaResp = await client.sendMedia(
        chatType: ChatTargetType.group,
        chatId: group.channelId,
        mediaBytes: Uint8List.fromList(utf8.encode('group payload bytes')),
        mediaKind: MediaKind.file,
        fileName: 'group-demo.txt',
        mimeType: 'text/plain',
        caption: 'group file upload',
      );
      print(
        'Group media success: ${groupMediaResp.success}, '
        'messageId: ${groupMediaResp.messageId}',
      );
    }

    final myId = reg.user?.id ?? 0;
    if (myId > 0) {
      print('Step 8: send private message to self');
      final pm = await client.sendPrivateMessage(myId, 'private self-test message');
      print(
        'Private message success: ${pm.success}, '
        'messageId: ${pm.messageId}, '
        'message: ${pm.messageText ?? ''}',
      );

      final voice = await _loadVoiceSample();
      if (voice != null) {
        print('Step 8.1: send voice to private chat');
        final privateVoiceResp = await client.sendMedia(
          chatType: ChatTargetType.private,
          chatId: myId,
          mediaBytes: voice,
          mediaKind: MediaKind.voice,
          fileName: 'sample.ogg',
          mimeType: 'audio/ogg',
          caption: 'voice to private',
        );
        print(
          'Private voice success: ${privateVoiceResp.success}, '
          'messageId: ${privateVoiceResp.messageId}',
        );

        if (channel.success && channel.channelId > 0) {
          print('Step 8.2: send voice to channel');
          final channelVoiceResp = await client.sendMedia(
            chatType: ChatTargetType.channel,
            chatId: channel.channelId,
            mediaBytes: voice,
            mediaKind: MediaKind.voice,
            fileName: 'sample.ogg',
            mimeType: 'audio/ogg',
            caption: 'voice to channel',
          );
          print(
            'Channel voice success: ${channelVoiceResp.success}, '
            'messageId: ${channelVoiceResp.messageId}',
          );
        }
      } else {
        print('Voice file not found. Skipping voice steps.');
      }

      final privateHistory = await client.getPrivateHistory(myId, limit: 20);
      print('Private history items: ${privateHistory.messages.length}');
      for (final item in privateHistory.messages.where((m) => m.attachment != null)) {
        _printAttachment(
          scope: 'private-history',
          contentType: item.contentType,
          text: item.content,
          attachment: item.attachment,
        );
      }
    }

    if (channel.success && channel.channelId > 0) {
      final channelHistory = await client.getChannelHistory(channel.channelId, limit: 20);
      print('Channel history items: ${channelHistory.messages.length}');
      for (final item in channelHistory.messages.where((m) => m.attachment != null)) {
        _printAttachment(
          scope: 'channel-history',
          contentType: item.contentType,
          text: item.content,
          attachment: item.attachment,
        );
      }
    }

    print('Step 9: ping');
    await client.ping();
    print('Ping sent');

    print('Done');
  } catch (e) {
    print('Complete example failed: $e');
  } finally {
    await privateSub?.cancel();
    await channelSub?.cancel();
    await client.disconnect();
    client.dispose();
  }
}

Future<Uint8List?> _loadVoiceSample() async {
  final envPath = Platform.environment['AEGIS_VOICE_PATH'];
  final candidates = <String>[
    if (envPath != null && envPath.isNotEmpty) envPath,
    'example/assets/sample.ogg',
    'AegisDartClient/example/assets/sample.ogg',
    'sample.ogg',
  ];

  for (final path in candidates) {
    final file = File(path);
    if (await file.exists()) {
      print('Using voice sample: ${file.path}');
      return file.readAsBytes();
    }
  }

  print('Provide .ogg file via AEGIS_VOICE_PATH or place it at example/assets/sample.ogg');
  return null;
}

void _printAttachment({
  required String scope,
  required MessageContentType contentType,
  required String? text,
  required ParsedMediaAttachment? attachment,
}) {
  if (attachment == null) return;
  final bytesLen = attachment.decodeBytes().length;
  print(
    '[$scope] type=$contentType text=${text ?? ''} '
    'file=${attachment.fileName} mime=${attachment.mimeType} bytes=$bytesLen',
  );
}
