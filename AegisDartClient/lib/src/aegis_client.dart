import 'dart:async';
import 'dart:typed_data';
import 'package:crypto/crypto.dart';
import 'src/message.dart';
import 'src/message_type.dart';
import 'src/message_encoder.dart';
import 'src/transport.dart';
import 'src/exceptions.dart';
import 'src/protocol_constants.dart';

/// Main Aegis client class
class AegisClient {
  late AegisTransport _transport;
  String? _authToken;
  bool _isAuthenticated = false;
  
  /// Stream of incoming messages
  Stream<Message> get messages => _transport.messages;
  
  /// Stream of disconnect events  
  Stream<void> get disconnects => _transport.disconnects;
  
  /// Check if client is connected to server
  bool get isConnected => _transport.isConnected;
  
  /// Check if client is authenticated
  bool get isAuthenticated => _isAuthenticated;

  /// Create new Aegis client
  AegisClient() {
    _transport = AegisTransport();
  }

  /// Connect to Aegis server
  Future<void> connect(String host, int port, {Duration? timeout}) async {
    await _transport.connect(host, port, timeout: timeout);
    
    // Send handshake message
    await _sendHandshake();
  }

  /// Authenticate with server
  Future<void> authenticate(String authToken) async {
    if (!_transport.isConnected) {
      throw NotConnectedException();
    }

    final message = Message.withType(MessageType.auth);
    message.payload = utf8.encode(authToken);
    message.flags = ProtocolConstants.flagRequiresAck;
    
    await _transport.sendMessage(message);
    _authToken = authToken;
    
    // Wait for ACK response (simplified - in real implementation should wait for specific response)
    await messages.firstWhere(
      (msg) => msg.type == MessageType.ack,
      orElse: () => throw TimeoutException('Authentication timeout', const Duration(seconds: 10))
    ).timeout(const Duration(seconds: 10));
    
    _isAuthenticated = true;
  }

  /// Send a text message
  Future<void> sendMessage(String text, {int? toUserId}) async {
    if (!_transport.isConnected) {
      throw NotConnectedException();
    }

    if (!_isAuthenticated) {
      throw Exception('Client is not authenticated');
    }

    // Create message payload: fromId(8) + toId(8) + messageType(1) + reserved(3) + text
    final payload = <int>[];
    
    // From user ID (placeholder - should be set after authentication)
    payload.addAll(_int64ToBytes(0)); 
    
    // To user ID (0 for broadcast)
    payload.addAll(_int64ToBytes(toUserId ?? 0));
    
    // Message type (0 = text)
    payload.add(0);
    
    // Reserved bytes
    payload.addAll([0, 0, 0]);
    
    // Message text
    payload.addAll(utf8.encode(text));
    
    final message = Message.withType(MessageType.message, payload);
    message.flags = ProtocolConstants.flagRequiresAck;
    
    await _transport.sendMessage(message);
  }

  /// Send ping message
  Future<void> ping() async {
    if (!_transport.isConnected) {
      throw NotConnectedException();
    }

    final timestamp = DateTime.now().millisecondsSinceEpoch;
    final message = Message.withType(MessageType.ping, _int64ToBytes(timestamp));
    
    await _transport.sendMessage(message);
  }

  /// Disconnect from server
  Future<void> disconnect() async {
    await _transport.disconnect();
    _isAuthenticated = false;
    _authToken = null;
  }

  /// Send initial handshake
  Future<void> _sendHandshake() async {
    final message = Message.withType(MessageType.handshake);
    
    // Create handshake payload: clientVersion(4) + nonce(12) + publicKey(var)
    final payload = <int>[];
    
    // Client version (placeholder)
    payload.addAll(_int32ToBytes(1000));
    
    // Nonce (12 random bytes)
    final nonce = _generateNonce();
    payload.addAll(nonce);
    
    // Public key (placeholder - should implement real key exchange)
    payload.addAll(utf8.encode('client_public_key_placeholder'));
    
    message.payload = payload;
    
    await _transport.sendMessage(message);
  }

  /// Convert int to 8-byte big-endian representation
  List<int> _int64ToBytes(int value) {
    final bytes = ByteData(8);
    bytes.setUint64(0, value, Endian.big);
    return bytes.buffer.asUint8List().toList();
  }

  /// Convert int to 4-byte big-endian representation  
  List<int> _int32ToBytes(int value) {
    final bytes = ByteData(4);
    bytes.setUint32(0, value, Endian.big);
    return bytes.buffer.asUint8List().toList();
  }

  /// Generate random nonce
  List<int> _generateNonce() {
    final random = DateTime.now().millisecondsSinceEpoch;
    final bytes = ByteData(12);
    bytes.setUint64(0, random, Endian.big);
    bytes.setUint32(8, random ~/ 1000, Endian.big);
    return bytes.buffer.asUint8List().toList();
  }

  /// Cleanup resources
  void dispose() {
    _transport.dispose();
  }
}
