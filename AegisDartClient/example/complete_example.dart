import 'dart:io';
import 'dart:async';
import 'package:aegis_client/aegis_client.dart';

/// Complete example demonstrating all Aegis Client features
/// including user management, channels, private messages, and error handling
class CompleteAegisExample {
  late AegisClient _client;
  User? _currentUser;
  Channel? _currentChannel;
  
  /// Run the complete example
  Future<void> run() async {
    // Setup logging
    AegisLogger.enabled = true;
    AegisLogger.level = LogLevel.debug;
    
    // Create client
    _client = AegisClient();
    
    // Setup message handlers
    _setupMessageHandlers();
    
    try {
      print('🚀 Starting Complete Aegis Client Example');
      
      // Step 1: Connect to server
      await _connectToServer();
      
      // Step 2: Register and authenticate
      await _registerAndAuthenticate();
      
      // Step 3: User search functionality
      await _demonstrateUserSearch();
      
      // Step 4: Channel operations
      await _demonstrateChannelOperations();
      
      // Step 5: Private messaging
      await _demonstratePrivateMessaging();
      
      // Step 6: Real-time message handling
      await _demonstrateRealTimeMessaging();
      
      print('✅ All demonstrations completed successfully!');
      
    } catch (e) {
      print('❌ Error in example: $e');
    } finally {
      await _cleanup();
    }
  }
  
  /// Connect to Aegis server
  Future<void> _connectToServer() async {
    print('\n📡 Connecting to server...');
    
    try {
      await _client.connect('localhost', 8888);
      print('✅ Connected to server successfully');
    } catch (e) {
      print('❌ Failed to connect: $e');
      rethrow;
    }
  }
  
  /// Register new user and authenticate
  Future<void> _registerAndAuthenticate() async {
    print('\n👤 User Registration & Authentication');
    
    try {
      // Register a new user
      final timestamp = DateTime.now().millisecondsSinceEpoch;
      final username = 'user_$timestamp';
      final email = 'user_$timestamp@example.com';
      
      print('📝 Registering user: $username');
      final registrationResponse = await _client.register(
        username,
        email,
        'secure_password_123',
        'generated_public_key_here',
      );
      
      if (registrationResponse.success && registrationResponse.user != null) {
        _currentUser = registrationResponse.user!;
        print('✅ User registered successfully');
        print('   ID: ${_currentUser!.id}');
        print('   Username: ${_currentUser!.username}');
        print('   Email: ${_currentUser!.email}');
      } else {
        print('❌ Registration failed: ${registrationResponse.message}');
        return;
      }
      
      // Authenticate
      print('🔐 Authenticating...');
      await _client.authenticate('auth_token_for_${_currentUser!.id}');
      print('✅ Authenticated successfully');
      
    } catch (e) {
      print('❌ Registration/Authentication error: $e');
      rethrow;
    }
  }
  
  /// Demonstrate user search functionality
  Future<void> _demonstrateUserSearch() async {
    if (_currentUser == null) return;
    
    print('\n🔍 User Search Demonstration');
    
    try {
      // Search for users by username pattern
      print('🔎 Searching for users with "user" in username...');
      final searchResponse = await _client.searchUsers('user', limit: 5);
      
      if (searchResponse.success) {
        print('✅ Found ${searchResponse.users.length} users:');
        for (final user in searchResponse.users) {
          print('   👤 ${user.username} (ID: ${user.id})');
          if (user.email != null) {
            print('      📧 ${user.email}');
          }
        }
      } else {
        print('❌ Search failed: ${searchResponse.message}');
      }
      
      // Search for specific user
      if (_currentUser != null) {
        print('🔎 Searching for current user: ${_currentUser!.username}');
        final specificSearch = await _client.searchUsers(_currentUser!.username, limit: 1);
        
        if (specificSearch.success && specificSearch.users.isNotEmpty) {
          final foundUser = specificSearch.users.first;
          print('✅ Found current user: ${foundUser.username}');
        }
      }
      
    } catch (e) {
      print('❌ User search error: $e');
    }
  }
  
  /// Demonstrate channel operations
  Future<void> _demonstrateChannelOperations() async {
    if (_currentUser == null) return;
    
    print('\n📢 Channel Operations Demonstration');
    
    try {
      // Create a public channel
      print('🏗️ Creating public channel...');
      final channelName = 'Test Channel ${DateTime.now().millisecondsSinceEpoch}';
      final createResponse = await _client.createChannel(
        channelName,
        description: 'A test channel for demonstrating Aegis client features',
        type: ChannelType.public,
      );
      
      if (createResponse.success && createResponse.channel != null) {
        _currentChannel = createResponse.channel!;
        print('✅ Channel created successfully');
        print('   📢 Name: ${_currentChannel!.name}');
        print('   🆔 ID: ${_currentChannel!.id}');
        print('   📝 Description: ${_currentChannel!.description ?? 'None'}');
        print('   🔓 Type: ${_currentChannel!.type}');
        print('   👥 Members: ${_currentChannel!.memberCount}');
        
        // Join the channel
        print('🚪 Joining channel...');
        final joinResponse = await _client.joinChannel(_currentChannel!.id);
        
        if (joinResponse.success) {
          print('✅ Joined channel successfully');
        } else {
          print('❌ Failed to join channel: ${joinResponse.message}');
        }
        
        // Send different types of messages to the channel
        await _sendChannelMessages();
        
      } else {
        print('❌ Channel creation failed: ${createResponse.message}');
      }
      
    } catch (e) {
      print('❌ Channel operations error: $e');
    }
  }
  
  /// Send various types of messages to a channel
  Future<void> _sendChannelMessages() async {
    if (_currentChannel == null) return;
    
    print('📨 Sending channel messages...');
    
    try {
      // Send a text message
      print('📝 Sending text message...');
      final textResponse = await _client.sendChannelMessage(
        _currentChannel!.id,
        'Hello from Dart client! 🎉',
        contentType: MessageContentType.text,
      );
      
      if (textResponse.success) {
        print('✅ Text message sent');
        if (textResponse.message != null) {
          print('   🆔 Message ID: ${textResponse.message!.id}');
          print('   ⏰ Created: ${textResponse.message!.createdAt}');
        }
      } else {
        print('❌ Failed to send text message: ${textResponse.messageText}');
      }
      
      // Send a message with reply
      if (textResponse.message != null) {
        print('💬 Sending reply message...');
        final replyResponse = await _client.sendChannelMessage(
          _currentChannel!.id,
          'This is a reply to the previous message! 👆',
          replyToMessageId: textResponse.message!.id,
        );
        
        if (replyResponse.success) {
          print('✅ Reply message sent');
        } else {
          print('❌ Failed to send reply: ${replyResponse.messageText}');
        }
      }
      
    } catch (e) {
      print('❌ Error sending channel messages: $e');
    }
  }
  
  /// Demonstrate private messaging
  Future<void> _demonstratePrivateMessaging() async {
    if (_currentUser == null) return;
    
    print('\n💬 Private Messaging Demonstration');
    
    try {
      // First, find another user to message
      print('🔎 Finding user to message...');
      final searchResponse = await _client.searchUsers('user', limit: 10);
      
      if (searchResponse.success && searchResponse.users.length > 1) {
        // Find a user that's not the current user
        final otherUser = searchResponse.users.firstWhere(
          (user) => user.id != _currentUser!.id,
          orElse: () => searchResponse.users.first,
        );
        
        print('👤 Found user: ${otherUser.username} (ID: ${otherUser.id})');
        
        // Send a private message
        print('📨 Sending private message...');
        final privateResponse = await _client.sendPrivateMessage(
          otherUser.id,
          'Hello ${otherUser.username}! This is a private message from ${_currentUser!.username} 🤖',
          contentType: MessageContentType.text,
        );
        
        if (privateResponse.success) {
          print('✅ Private message sent successfully');
          if (privateResponse.message != null) {
            print('   🆔 Message ID: ${privateResponse.message!.id}');
            print('   📅 Sent: ${privateResponse.message!.createdAt}');
            print('   📤 To: ${otherUser.username}');
          }
          
          if (privateResponse.privateChat != null) {
            print('   💬 Private chat ID: ${privateResponse.privateChat!.id}');
          }
        } else {
          print('❌ Failed to send private message: ${privateResponse.messageText}');
        }
        
      } else {
        print('ℹ️ No other users found for private messaging demo');
      }
      
    } catch (e) {
      print('❌ Private messaging error: $e');
    }
  }
  
  /// Demonstrate real-time message handling
  Future<void> _demonstrateRealTimeMessaging() async {
    print('\n⚡ Real-time Message Handling');
    print('👂 Listening for incoming messages for 10 seconds...');
    
    // Message handling is already set up in _setupMessageHandlers()
    await Future.delayed(const Duration(seconds: 10));
    print('⏹️ Real-time demo completed');
  }
  
  /// Setup message handlers for different message types
  void _setupMessageHandlers() {
    print('🔧 Setting up message handlers...');
    
    _client.messages.listen((message) {
      switch (message.type) {
        case MessageType.message:
          _handleBasicMessage(message);
          break;
          
        case MessageType.channelMessage:
          _handleChannelMessage(message);
          break;
          
        case MessageType.privateChatMessage:
          _handlePrivateMessage(message);
          break;
          
        case MessageType.userSearchResult:
          _handleUserSearchResult(message);
          break;
          
        case MessageType.registerResponse:
          _handleRegistrationResponse(message);
          break;
          
        case MessageType.ping:
          _handlePingMessage(message);
          break;
          
        case MessageType.error:
          _handleErrorMessage(message);
          break;
          
        default:
          print('📩 Received unhandled message type: ${message.type}');
      }
    });
    
    _client.disconnects.listen((_) {
      print('🔌 Disconnected from server');
    });
  }
  
  /// Handle basic text messages
  void _handleBasicMessage(Message message) {
    try {
      final text = String.fromCharCodes(message.payload.sublist(21));
      print('📨 Basic message: $text');
    } catch (e) {
      print('❌ Error parsing basic message: $e');
    }
  }
  
  /// Handle channel messages
  void _handleChannelMessage(Message message) {
    print('📢 Channel message received');
    try {
      // In a real implementation, you'd parse the JSON payload
      // For now, just acknowledge receipt
      print('   🆔 Sequence ID: ${message.sequenceId}');
    } catch (e) {
      print('❌ Error parsing channel message: $e');
    }
  }
  
  /// Handle private messages
  void _handlePrivateMessage(Message message) {
    print('💬 Private message received');
    try {
      print('   🆔 Sequence ID: ${message.sequenceId}');
      // In a real implementation, parse the JSON payload
    } catch (e) {
      print('❌ Error parsing private message: $e');
    }
  }
  
  /// Handle user search results
  void _handleUserSearchResult(Message message) {
    print('🔍 User search result received');
    try {
      // In a real implementation, parse the JSON payload
      print('   📊 Payload size: ${message.payload.length} bytes');
    } catch (e) {
      print('❌ Error parsing search result: $e');
    }
  }
  
  /// Handle registration responses
  void _handleRegistrationResponse(Message message) {
    print('📝 Registration response received');
    try {
      // In a real implementation, parse the JSON payload
      print('   🆔 Sequence ID: ${message.sequenceId}');
    } catch (e) {
      print('❌ Error parsing registration response: $e');
    }
  }
  
  /// Handle ping messages
  void _handlePingMessage(Message message) {
    try {
      final timestamp = _bytesToInt64(message.payload);
      final latency = DateTime.now().millisecondsSinceEpoch - timestamp;
      print('🏓 Ping received: ${latency}ms latency');
    } catch (e) {
      print('❌ Error parsing ping message: $e');
    }
  }
  
  /// Handle error messages
  void _handleErrorMessage(Message message) {
    try {
      final errorCode = _bytesToUint16(message.payload.sublist(0, 2));
      final errorText = String.fromCharCodes(message.payload.sublist(4));
      print('❌ Error $errorCode: $errorText');
    } catch (e) {
      print('❌ Error parsing error message: $e');
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
  
  /// Cleanup resources
  Future<void> _cleanup() async {
    print('\n🧹 Cleaning up...');
    
    try {
      await _client.disconnect();
      _client.dispose();
      print('✅ Cleanup completed');
    } catch (e) {
      print('❌ Cleanup error: $e');
    }
  }
}

/// Main entry point
void main() async {
  final example = CompleteAegisExample();
  await example.run();
}
