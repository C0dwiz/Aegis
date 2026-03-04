import 'dart:io';
import 'package:aegis_client/aegis_client.dart';

/// Example usage of Aegis Client Library with new features
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
      _handleIncomingMessage(message);
    });

    // Listen for disconnect events
    client.disconnects.listen((_) {
      print('Disconnected from server');
    });

    // Example 1: Register a new user
    print('\n--- User Registration ---');
    try {
      final registrationResponse = await client.register(
        'testuser',
        'test@example.com',
        'password123',
        'public_key_placeholder',
      );
      
      if (registrationResponse.success) {
        print('User registered successfully!');
        if (registrationResponse.user != null) {
          print('User ID: ${registrationResponse.user!.id}');
          print('Username: ${registrationResponse.user!.username}');
        }
      } else {
        print('Registration failed: ${registrationResponse.message}');
      }
    } catch (e) {
      print('Registration error: $e');
    }

    // Authenticate (if registration was successful or using existing credentials)
    print('\n--- Authentication ---');
    try {
      await client.authenticate('your_auth_token_here');
      print('Authenticated successfully!');
    } catch (e) {
      print('Authentication failed: $e');
      // Continue with demo for unauthenticated operations
    }

    // Example 2: Search for users (requires authentication)
    if (client.isAuthenticated) {
      print('\n--- User Search ---');
      try {
        final searchResponse = await client.searchUsers('test', limit: 10);
        
        if (searchResponse.success) {
          print('Found ${searchResponse.users.length} users:');
          for (final user in searchResponse.users) {
            print('  - ${user.username} (ID: ${user.id})');
            if (user.email != null) {
              print('    Email: ${user.email}');
            }
          }
        } else {
          print('Search failed: ${searchResponse.message}');
        }
      } catch (e) {
        print('Search error: $e');
      }
    }

    // Example 3: Create a channel (requires authentication)
    if (client.isAuthenticated) {
      print('\n--- Channel Creation ---');
      try {
        final channelResponse = await client.createChannel(
          'Test Channel',
          description: 'A test channel created from Dart client',
          type: ChannelType.public,
        );
        
        if (channelResponse.success && channelResponse.channel != null) {
          final channel = channelResponse.channel!;
          print('Channel created successfully!');
          print('Channel ID: ${channel.id}');
          print('Channel Name: ${channel.name}');
          print('Channel Type: ${channel.type}');
          
          // Join the channel
          print('\n--- Joining Channel ---');
          final joinResponse = await client.joinChannel(channel.id);
          if (joinResponse.success) {
            print('Joined channel successfully!');
          } else {
            print('Failed to join channel: ${joinResponse.message}');
          }
          
          // Send a message to the channel
          print('\n--- Sending Channel Message ---');
          final messageResponse = await client.sendChannelMessage(
            channel.id,
            'Hello from Dart client! This is a test message.',
          );
          
          if (messageResponse.success) {
            print('Channel message sent successfully!');
            if (messageResponse.message != null) {
              print('Message ID: ${messageResponse.message!.id}');
            }
          } else {
            print('Failed to send channel message: ${messageResponse.messageText}');
          }
        } else {
          print('Channel creation failed: ${channelResponse.message}');
        }
      } catch (e) {
        print('Channel operations error: $e');
      }
    }

    // Example 4: Send a private message (requires authentication)
    if (client.isAuthenticated) {
      print('\n--- Private Message ---');
      try {
        // Note: Replace with actual user ID from search results
        final targetUserId = 12345;
        final privateResponse = await client.sendPrivateMessage(
          targetUserId,
          'Hello! This is a private message from Dart client.',
        );
        
        if (privateResponse.success) {
          print('Private message sent successfully!');
          if (privateResponse.message != null) {
            print('Message ID: ${privateResponse.message!.id}');
          }
        } else {
          print('Failed to send private message: ${privateResponse.messageText}');
        }
      } catch (e) {
        print('Private message error: $e');
      }
    }

    // Example 5: Send a basic text message (legacy method)
    print('\n--- Basic Message ---');
    try {
      await client.sendMessage('Hello from Dart client! (legacy method)');
      print('Basic message sent!');
    } catch (e) {
      print('Basic message error: $e');
    }

    // Example 6: Send ping
    print('\n--- Ping ---');
    try {
      await client.ping();
      print('Ping sent!');
    } catch (e) {
      print('Ping error: $e');
    }

    // Keep connection alive for demonstration
    print('\nPress Ctrl+C to disconnect...');
    await Future.delayed(const Duration(seconds: 30));

  } catch (e) {
    print('Error: $e');
  } finally {
    // Cleanup
    print('\nDisconnecting...');
    await client.disconnect();
    client.dispose();
    print('Done!');
  }
}

/// Handle incoming messages
void _handleIncomingMessage(Message message) {
  switch (message.type) {
    case MessageType.message:
      final text = String.fromCharCodes(message.payload.sublist(21)); // Skip header
      print('Message: $text');
      break;
      
    case MessageType.ping:
      final timestamp = _bytesToInt64(message.payload);
      final latency = DateTime.now().millisecondsSinceEpoch - timestamp;
      print('Ping: ${latency}ms');
      break;
      
    case MessageType.error:
      final errorCode = _bytesToUint16(message.payload.sublist(0, 2));
      final errorText = String.fromCharCodes(message.payload.sublist(4));
      print('Error $errorCode: $errorText');
      break;

    case MessageType.channelMessage:
      print('Channel message received');
      // Handle channel message payload
      break;

    case MessageType.privateChatMessage:
      print('Private message received');
      // Handle private message payload
      break;

    case MessageType.userSearchResult:
      print('User search result received');
      // Handle search result
      break;

    case MessageType.registerResponse:
      print('Registration response received');
      // Handle registration response
      break;

    default:
      print('Unknown message type: ${message.type}');
  }
}

/// Convert bytes to 64-bit integer
int _bytesToInt64(List<int> bytes) {
  if (bytes.length < 8) return 0;
  int value = 0;
  for (int i = 0; i < 8; i++) {
    value = (value << 8) | bytes[i];
  }
  return value;
}

/// Convert bytes to 16-bit unsigned integer
int _bytesToUint16(List<int> bytes) {
  if (bytes.length < 2) return 0;
  return (bytes[0] << 8) | bytes[1];
}
