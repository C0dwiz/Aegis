import 'dart:async';
import 'dart:io';
import 'package:aegis_client/aegis_client.dart';

/// Advanced example with error handling and reconnection
class AdvancedAegisClient {
  late AegisClient _client;
  final String host;
  final int port;
  final String authToken;
  
  bool _isRunning = false;
  Timer? _pingTimer;
  Timer? _reconnectTimer;

  AdvancedAegisClient({
    required this.host,
    required this.port,
    required this.authToken,
  });

  /// Start the client with automatic reconnection
  Future<void> start() async {
    _isRunning = true;
    AegisLogger.info('Starting Aegis client...');
    
    await _connect();
    _startPingTimer();
  }

  /// Stop the client
  Future<void> stop() async {
    _isRunning = false;
    _pingTimer?.cancel();
    _reconnectTimer?.cancel();
    
    if (_client.isConnected) {
      await _client.disconnect();
    }
    
    _client.dispose();
    AegisLogger.info('Aegis client stopped');
  }

  /// Connect to server with retry logic
  Future<void> _connect() async {
    while (_isRunning) {
      try {
        _client = AegisClient();
        
        // Setup message handlers
        _setupMessageHandlers();
        
        // Connect
        await _client.connect(host, port);
        
        // Authenticate
        await _client.authenticate(authToken);
        
        AegisLogger.info('Connected and authenticated successfully');
        return;
        
      } catch (e) {
        AegisLogger.error('Connection failed, retrying in 5 seconds...', e);
        
        if (!_isRunning) break;
        
        await Future.delayed(const Duration(seconds: 5));
      }
    }
  }

  /// Setup message and disconnect handlers
  void _setupMessageHandlers() {
    // Handle incoming messages
    _client.messages.listen((message) {
      switch (message.type) {
        case MessageType.message:
          _handleChatMessage(message);
          break;
        case MessageType.ping:
          _handlePingMessage(message);
          break;
        case MessageType.error:
          _handleErrorMessage(message);
          break;
        default:
          AegisLogger.debug('Received unhandled message type: ${message.type}');
      }
    });

    // Handle disconnections
    _client.disconnects.listen((_) {
      AegisLogger.warning('Disconnected from server');
      
      if (_isRunning) {
        _scheduleReconnect();
      }
    });
  }

  /// Handle chat messages
  void _handleChatMessage(Message message) {
    try {
      // Parse message payload: fromId(8) + toId(8) + messageType(1) + reserved(3) + text
      if (message.payload.length >= 21) {
        final fromId = _bytesToInt64(message.payload.sublist(0, 8));
        final toId = _bytesToInt64(message.payload.sublist(8, 16));
        final messageType = message.payload[20];
        final text = String.fromCharCodes(message.payload.sublist(21));
        
        AegisLogger.info('Chat message from $fromId to $toId: $text');
        
        // Handle message based on type
        if (messageType == 0) { // Text message
          print('💬 [$fromId]: $text');
        }
      }
    } catch (e) {
      AegisLogger.error('Error parsing chat message', e);
    }
  }

  /// Handle ping messages
  void _handlePingMessage(Message message) {
    if (message.payload.length >= 8) {
      final timestamp = _bytesToInt64(message.payload);
      final latency = DateTime.now().millisecondsSinceEpoch - timestamp;
      AegisLogger.debug('Ping response: ${latency}ms');
    }
  }

  /// Handle error messages
  void _handleErrorMessage(Message message) {
    if (message.payload.length >= 4) {
      final errorCode = _bytesToUint16(message.payload.sublist(0, 2));
      final errorMessage = String.fromCharCodes(message.payload.sublist(4));
      AegisLogger.error('Server error $errorCode: $errorMessage');
    }
  }

  /// Send a message with error handling
  Future<void> sendMessage(String text, {int? toUserId}) async {
    try {
      await _client.sendMessage(text, toUserId: toUserId);
      AegisLogger.info('Message sent successfully');
    } catch (e) {
      AegisLogger.error('Failed to send message', e);
    }
  }

  /// Start periodic ping timer
  void _startPingTimer() {
    _pingTimer = Timer.periodic(const Duration(seconds: 30), (_) {
      if (_client.isConnected) {
        _client.ping();
      }
    });
  }

  /// Schedule reconnection attempt
  void _scheduleReconnect() {
    _reconnectTimer?.cancel();
    _reconnectTimer = Timer(const Duration(seconds: 5), () {
      if (_isRunning) {
        _connect();
      }
    });
  }

  /// Helper: convert bytes to int64
  int _bytesToInt64(List<int> bytes) {
    int result = 0;
    for (int i = 0; i < 8; i++) {
      result = (result << 8) | bytes[i];
    }
    return result;
  }

  /// Helper: convert bytes to uint16
  int _bytesToUint16(List<int> bytes) {
    return (bytes[0] << 8) | bytes[1];
  }
}

void main() async {
  // Configure logging
  AegisLogger.enabled = true;
  AegisLogger.level = LogLevel.info;

  final client = AdvancedAegisClient(
    host: 'localhost',
    port: 8888,
    authToken: 'your_auth_token_here',
  );

  // Handle Ctrl+C gracefully
  ProcessSignal.sigint.watch().listen((signal) async {
    print('\nShutting down...');
    await client.stop();
    exit(0);
  });

  // Start the client
  await client.start();

  // Send some test messages
  await Future.delayed(const Duration(seconds: 2));
  await client.sendMessage('Hello from advanced client!');

  // Keep running
  print('Client is running. Press Ctrl+C to stop.');
  
  // Simulate some activity
  int counter = 0;
  while (true) {
    await Future.delayed(const Duration(seconds: 10));
    counter++;
    await client.sendMessage('Auto message #$counter');
  }
}
