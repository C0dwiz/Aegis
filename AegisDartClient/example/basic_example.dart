import 'dart:convert';
import 'package:aegis_client/aegis_client.dart';

/// Basic smoke example for the current Aegis protocol flow.
void main() async {
  AegisLogger.enabled = true;
  AegisLogger.level = LogLevel.info;

  final client = AegisClient();

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

    print('Sending channel message...');
    final channelMsg = await client.sendChannelMessage(channel.channelId, 'hello from dart basic');
    print('Channel message success: ${channelMsg.success}, messageId: ${channelMsg.messageId}');

    print('Sending private message to self...');
    final myId = reg.user?.id ?? 0;
    if (myId > 0) {
      final pm = await client.sendPrivateMessage(myId, 'self private message from dart basic');
      print('Private message success: ${pm.success}, messageId: ${pm.messageId}');
    }

    print('Sending ping...');
    await client.ping();
    print('Ping sent');

  } catch (e) {
    print('Smoke test failed: $e');
  } finally {
    print('Disconnecting...');
    await client.disconnect();
    client.dispose();
    print('Done');
  }
}
