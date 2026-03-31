import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';
import 'package:aegis_client/aegis_client.dart';

/// Basic smoke example for the current Aegis protocol flow.
void main() async {
  AegisLogger.enabled = true;
  AegisLogger.level = LogLevel.info;

  final client = AegisClient.official();
  StreamSubscription<PrivateChatMessageEvent>? privateSub;
  StreamSubscription<ChannelMessageEvent>? channelSub;

  try {
    print('=== Aegis Dart Smoke Test ===');
    print('Using api_id=${client.apiCredentials?.appId ?? 'none'}');
    print('Connecting...');
    await client.connect('localhost', 8888);
    print('Connected');

    final suffix = DateTime.now().millisecondsSinceEpoch;
    final username = 'dart_$suffix';
    final email = 'dart_$suffix@example.com';
    const password = 'test_password_123';

    print('Registering user: $username');
    final reg = await client.register(
      username,
      email,
      password,
      'dart_public_key_placeholder',
    );
    if (!reg.success) {
      throw Exception('Registration failed: ${reg.message}');
    }
    print('Registered user id: ${reg.user?.id ?? 0}');

    print('Authenticating with username/password...');
    await client.authenticate(jsonEncode({
      'Username': username,
      'Password': password,
      'ClientInfo': 'aegis-dart-basic-example'
    }));
    print('Authenticated');

    privateSub = client.events.onPrivateMessageEvent((event) {
      if (event.contentType == MessageContentType.audio &&
          event.attachment != null) {
        final voice = event.attachment!;
        print(
            '[event/private/voice] id=${event.id} from=${event.fromUserId} file=${voice.fileName} mime=${voice.mimeType} bytes=${voice.decodeBytes().length}');
      } else {
        print(
            '[event/private] id=${event.id} from=${event.fromUserId} text=${event.content}');
      }
    });

    channelSub = client.events.onChannelMessageEvent((event) {
      if (event.contentType == MessageContentType.audio &&
          event.attachment != null) {
        final voice = event.attachment!;
        print(
            '[event/channel/voice] id=${event.id} channel=${event.channelId} file=${voice.fileName} mime=${voice.mimeType} bytes=${voice.decodeBytes().length}');
      } else {
        print(
            '[event/channel] id=${event.id} channel=${event.channelId} text=${event.content}');
      }
    });

    print('Subscribed to private/channel event streams');

    print('Searching users by prefix "dart_"');
    final search = await client.searchUsers('dart_', limit: 5);
    print('Search success: ${search.success}, users: ${search.users.length}');

    print('Creating channel...');
    final channel = await client.createChannel(
      'dart-channel-$suffix',
      description: 'Channel from basic Dart example',
      type: ChannelType.public,
    );
    if (!channel.success || channel.channelId == 0) {
      throw Exception('Channel creation failed: ${channel.message}');
    }
    print('Channel created id: ${channel.channelId}');

    print('Loading chat list...');
    final chatList = await client.getChatList();
    print(
        'Chat list success: ${chatList.success}, chats: ${chatList.chats.length}');
    for (final chat in chatList.chats.take(5)) {
      print(
          '  - chatId=${chat.chatId} type=${chat.type} title=${chat.title} unread=${chat.unreadCount}');
    }

    print('Sending channel message...');
    final channelMsg = await client.sendChannelMessage(
        channel.channelId, 'hello from dart basic');
    print(
        'Channel message success: ${channelMsg.success}, messageId: ${channelMsg.messageId}');

    print('Sending private message to self...');
    final myId = reg.user?.id ?? 0;
    if (myId > 0) {
      final pm = await client.sendPrivateMessage(
          myId, 'self private message from dart basic');
      print(
          'Private message success: ${pm.success}, messageId: ${pm.messageId}');

      // Voice message example (dummy ogg bytes for protocol demo)
      final voiceBytes =
          Uint8List.fromList([0x4F, 0x67, 0x67, 0x53, 0x00, 0x02, 0x00, 0x00]);
      final voiceResp = await client.sendMedia(
        chatType: ChatTargetType.private,
        chatId: myId,
        mediaBytes: voiceBytes,
        mediaKind: MediaKind.voice,
        fileName: 'voice-note.ogg',
        mimeType: 'audio/ogg',
        caption: 'voice check',
      );
      print(
          'Voice message success: ${voiceResp.success}, messageId: ${voiceResp.messageId}');

      final privateHistory = await client.getPrivateHistory(myId, limit: 10);
      print(
          'Private history success: ${privateHistory.success}, messages: ${privateHistory.messages.length}');
    }

    final channelHistory =
        await client.getChannelHistory(channel.channelId, limit: 10);
    print(
        'Channel history success: ${channelHistory.success}, messages: ${channelHistory.messages.length}');

    print('Sending ping...');
    await client.ping();
    print('Ping sent');
  } catch (e) {
    print('Smoke test failed: $e');
  } finally {
    print('Disconnecting...');
    await privateSub?.cancel();
    await channelSub?.cancel();
    await client.disconnect();
    client.dispose();
    print('Done');
  }
}
