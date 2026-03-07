import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:aegis_client/aegis_client.dart';

/// Advanced example with reconnect loop and periodic protocol checks.
class AdvancedAegisClient {
  AdvancedAegisClient({
    required this.host,
    required this.port,
    required this.username,
    required this.password,
  });

  final String host;
  final int port;
  final String username;
  final String password;

  AegisClient? _client;
  bool _running = false;
  Timer? _heartbeatTimer;

  Future<void> start() async {
    _running = true;
    while (_running) {
      try {
        await _connectAndAuthenticate();
        _startHeartbeat();

        // Keep this session alive until disconnect.
        await _client!.disconnects.first;
        _stopHeartbeat();
      } catch (e) {
        stderr.writeln('Session error: $e');
      }

      if (_running) {
        stderr.writeln('Reconnecting in 3 seconds...');
        await Future.delayed(const Duration(seconds: 3));
      }
    }
  }

  Future<void> stop() async {
    _running = false;
    _stopHeartbeat();
    await _client?.disconnect();
    _client?.dispose();
  }

  Future<void> _connectAndAuthenticate() async {
    _client?.dispose();
    _client = AegisClient();

    await _client!.connect(host, port);

    await _client!.authenticate(jsonEncode({
      'Username': username,
      'Password': password,
      'ClientInfo': 'aegis-dart-advanced-example'
    }));

    stdout.writeln('Connected and authenticated as $username');
  }

  void _startHeartbeat() {
    _heartbeatTimer = Timer.periodic(const Duration(seconds: 15), (_) async {
      try {
        if (_client == null || !_client!.isConnected || !_client!.isAuthenticated) {
          return;
        }

        await _client!.ping();
        final search = await _client!.searchUsers('dart_', limit: 3);
        stdout.writeln(
          'Heartbeat ok: ping sent, search users=${search.users.length}',
        );
      } catch (e) {
        stderr.writeln('Heartbeat failed: $e');
      }
    });
  }

  void _stopHeartbeat() {
    _heartbeatTimer?.cancel();
    _heartbeatTimer = null;
  }
}

Future<void> main() async {
  AegisLogger.enabled = true;
  AegisLogger.level = LogLevel.info;

  final userSuffix = DateTime.now().millisecondsSinceEpoch;
  final username = 'dart_adv_$userSuffix';
  final password = 'test_password_123';

  // Create user once for the advanced session.
  final bootstrapClient = AegisClient();
  try {
    await bootstrapClient.connect('localhost', 8888);
    await bootstrapClient.register(
      username,
      'dart_adv_$userSuffix@example.com',
      password,
      'dart_public_key_placeholder',
    );
  } finally {
    await bootstrapClient.disconnect();
    bootstrapClient.dispose();
  }

  final client = AdvancedAegisClient(
    host: 'localhost',
    port: 8888,
    username: username,
    password: password,
  );

  ProcessSignal.sigint.watch().listen((_) async {
    await client.stop();
    exit(0);
  });

  await client.start();
}
