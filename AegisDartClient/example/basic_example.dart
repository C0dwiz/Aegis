import 'dart:async';
import 'dart:convert';
import 'package:aegis_client/aegis_client.dart';

/// Basic smoke example for the current Aegis protocol flow.
void main() async {
  AegisLogger.enabled = true;
  AegisLogger.level = LogLevel.info;

  final client = AegisClient();
  StreamSubscription<PrivateChatMessageEvent>? privateSub;
  StreamSubscription<ChannelMessageEvent>? channelSub;

  try {
    print('=== Aegis Dart Smoke Test ===');
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
      print('[event/private] id=${event.id} from=${event.fromUserId} text=${event.content}');
    });

    channelSub = client.events.onChannelMessageEvent((event) {
      print('[event/channel] id=${event.id} channel=${event.channelId} text=${event.content}');
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
    print('Chat list success: ${chatList.success}, chats: ${chatList.chats.length}');
    for (final chat in chatList.chats.take(5)) {
      print('  - chatId=${chat.chatId} type=${chat.type} title=${chat.title} unread=${chat.unreadCount}');
    }

    print('Sending channel message...');
    final channelMsg = await client.sendChannelMessage(channel.channelId, 'hello from dart basic');
    print('Channel message success: ${channelMsg.success}, messageId: ${channelMsg.messageId}');

    print('Sending private message to self...');
    final myId = reg.user?.id ?? 0;
    if (myId > 0) {
      final pm = await client.sendPrivateMessage(myId, 'self private message from dart basic');
      print('Private message success: ${pm.success}, messageId: ${pm.messageId}');

      final privateHistory = await client.getPrivateHistory(myId, limit: 10);
      print('Private history success: ${privateHistory.success}, messages: ${privateHistory.messages.length}');
    }

    final channelHistory = await client.getChannelHistory(channel.channelId, limit: 10);
    print('Channel history success: ${channelHistory.success}, messages: ${channelHistory.messages.length}');

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
