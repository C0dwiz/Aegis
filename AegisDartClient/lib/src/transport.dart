import 'dart:async';
import 'dart:io';
import 'dart:typed_data';
import 'message.dart';
import 'message_encoder.dart';
import 'exceptions.dart';
import 'logger.dart';

/// TCP transport layer for Aegis client communication
class AegisTransport {
  late Socket _socket;
  bool _isConnected = false;
  int _nextSequenceId = 1;
  
  final StreamController<Message> _messageController = StreamController<Message>.broadcast();
  final StreamController<void> _disconnectController = StreamController<void>.broadcast();
  
  /// Stream of incoming messages
  Stream<Message> get messages => _messageController.stream;
  
  /// Stream of disconnect events
  Stream<void> get disconnects => _disconnectController.stream;
  
  /// Check if client is connected to server
  bool get isConnected => _isConnected;

  /// Connect to Aegis server
  Future<void> connect(String host, int port, {Duration? timeout}) async {
    if (_isConnected) {
      throw ConnectionException('Already connected to server');
    }

    AegisLogger.info('Connecting to $host:$port');
    
    try {
      _socket = await Socket.connect(host, port, timeout: timeout ?? const Duration(seconds: 10))
          .timeout(timeout ?? const Duration(seconds: 10));
      
      _isConnected = true;
      _nextSequenceId = 1;
      
      AegisLogger.info('Connected to $host:$port');
      
      // Start listening for incoming data
      _listenForMessages();
      
    } catch (e) {
      _isConnected = false;
      AegisLogger.error('Failed to connect to $host:$port', e);
      throw ConnectionException('Failed to connect to $host:$port', e);
    }
  }

  /// Disconnect from server
  Future<void> disconnect() async {
    if (!_isConnected) return;
    
    _isConnected = false;
    AegisLogger.info('Disconnecting from server');
    
    try {
      await _socket.close();
    } catch (e) {
      // Ignore errors during disconnect
    }
    
    _disconnectController.add(null);
  }

  /// Send a message to the server
  Future<void> sendMessage(Message message) async {
    if (!_isConnected) {
      throw NotConnectedException();
    }

    AegisLogger.debug('Sending message: ${message.type} (seq: ${message.sequenceId})');

    try {
      // Set sequence ID if not set
      if (message.sequenceId == 0) {
        message.sequenceId = _getNextSequenceId();
      }

      // Encode and send message
      final data = MessageEncoder.encode(message);
      _socket.add(data);
      await _socket.flush();
      
      AegisLogger.debug('Message sent successfully');
      
    } catch (e) {
      _isConnected = false;
      _disconnectController.add(null);
      AegisLogger.error('Failed to send message', e);
      throw ConnectionException('Failed to send message', e);
    }
  }

  /// Get next sequence ID
  int _getNextSequenceId() => _nextSequenceId++;

  /// Listen for incoming messages
  void _listenForMessages() {
    _socket.listen(
      (Uint8List data) {
        _handleIncomingData(data);
      },
      onError: (error) {
        _isConnected = false;
        _disconnectController.add(null);
      },
      onDone: () {
        _isConnected = false;
        _disconnectController.add(null);
      },
    );
  }

  /// Handle incoming data and parse messages
  void _handleIncomingData(Uint8List data) {
    try {
      final message = MessageEncoder.decode(data);
      AegisLogger.debug('Received message: ${message.type} (seq: ${message.sequenceId})');
      _messageController.add(message);
    } catch (e) {
      // Log error but don't disconnect - might be corrupted data
      AegisLogger.error('Error parsing message', e);
    }
  }

  /// Cleanup resources
  void dispose() {
    if (_isConnected) {
      disconnect();
    }
    
    _messageController.close();
    _disconnectController.close();
  }
}
