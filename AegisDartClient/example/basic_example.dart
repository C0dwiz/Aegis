import 'dart:io';
import 'package:aegis_client/aegis_client.dart';

/// Example usage of Aegis Client Library
void main() async {
  // Enable debug logging
  AegisLogger.enabled = true;
  AegisLogger.level = LogLevel.debug;

  // Create client instance
  final client = AegisClient();

  try {
    print('=== Aegis Client Example ===');
    
    // Connect to server
    print('Connecting to server...');
    await client.connect('localhost', 8888);
    print('Connected successfully!');

    // Listen for incoming messages
    client.messages.listen((message) {
      print('Received message: ${message.type}');
      if (message.type == MessageType.message) {
        final text = String.fromCharCodes(message.payload.sublist(21)); // Skip header
        print('Text: $text');
      }
    });

    // Listen for disconnect events
    client.disconnects.listen((_) {
      print('Disconnected from server');
    });

    // Authenticate (if required)
    print('Authenticating...');
    await client.authenticate('your_auth_token_here');
    print('Authenticated successfully!');

    // Send a text message
    print('Sending message...');
    await client.sendMessage('Hello from Dart client!');
    print('Message sent!');

    // Send ping
    print('Sending ping...');
    await client.ping();
    print('Ping sent!');

    // Keep connection alive for demonstration
    print('Press Ctrl+C to disconnect...');
    await Future.delayed(const Duration(seconds: 30));

  } catch (e) {
    print('Error: $e');
  } finally {
    // Cleanup
    print('Disconnecting...');
    await client.disconnect();
    client.dispose();
    print('Done!');
  }
}
