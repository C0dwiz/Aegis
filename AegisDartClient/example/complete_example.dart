import 'dart:convert';

import 'package:aegis_client/aegis_client.dart';

/// Complete protocol-oriented demo for the current server behavior.
Future<void> main() async {
  AegisLogger.enabled = true;
  AegisLogger.level = LogLevel.info;

  final client = AegisClient();
  final suffix = DateTime.now().millisecondsSinceEpoch;
  final username = 'dart_complete_$suffix';
  final password = 'test_password_123';

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

    print('Step 4: search users');
    final search = await client.searchUsers('dart_', limit: 10);
    print('Search success: ${search.success}');
    for (final u in search.users) {
      print('  user ${u.id}: ${u.username}');
    }

    print('Step 5: create channel');
    final channel = await client.createChannel(
      'dart-complete-channel-$suffix',
      description: 'Channel from complete Dart example',
      type: ChannelType.public,
    );
    print('Channel success: ${channel.success}, channelId: ${channel.channelId}');

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
    }

    print('Step 9: ping');
    await client.ping();
    print('Ping sent');

    print('Done');
  } catch (e) {
    print('Complete example failed: $e');
  } finally {
    await client.disconnect();
    client.dispose();
  }
}
