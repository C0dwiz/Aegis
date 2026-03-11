import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';
import 'message.dart';
import 'message_encoder.dart';
import 'exceptions.dart';
import 'logger.dart';
import 'protocol_constants.dart';

/// TCP transport layer for Aegis client communication
class AegisTransport {
  late Socket _socket;
  bool _isConnected = false;
  int _nextSequenceId = 1;
  Uint8List _pendingBytes = Uint8List(0);
  Uint8List _transportMaskingKey = Uint8List(0);
  int _inboundMaskOffset = 0;
  int _outboundMaskOffset = 0;
  
  final StreamController<Message> _messageController = StreamController<Message>.broadcast();
  final StreamController<void> _disconnectController = StreamController<void>.broadcast();
  
  /// Stream of incoming messages
  Stream<Message> get messages => _messageController.stream;
  
  /// Stream of disconnect events
  Stream<void> get disconnects => _disconnectController.stream;
  
  /// Check if client is connected to server
  bool get isConnected => _isConnected;

  /// Connect to Aegis server
  Future<void> connect(
    String host,
    int port, {
    Duration? timeout,
    String? transportMaskingKey,
  }) async {
    if (_isConnected) {
      throw ConnectionException('Already connected to server');
    }

    AegisLogger.info('Connecting to $host:$port');
    
    try {
      _socket = await Socket.connect(host, port, timeout: timeout ?? const Duration(seconds: 10))
          .timeout(timeout ?? const Duration(seconds: 10));
      
      _isConnected = true;
      _nextSequenceId = 1;
        _pendingBytes = Uint8List(0);
        _inboundMaskOffset = 0;
        _outboundMaskOffset = 0;
        _transportMaskingKey = (transportMaskingKey != null && transportMaskingKey.trim().isNotEmpty)
          ? Uint8List.fromList(utf8.encode(transportMaskingKey))
          : Uint8List(0);
      
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
      final outgoing = _applyOutboundMask(data);
      _socket.add(outgoing);
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
        AegisLogger.error('Socket error', error);
        _isConnected = false;
        if (!_disconnectController.isClosed) _disconnectController.add(null);
      },
      onDone: () {
        _isConnected = false;
        if (!_disconnectController.isClosed) _disconnectController.add(null);
      },
    );
  }

  /// Handle incoming data and parse messages
  void _handleIncomingData(Uint8List data) {
    if (data.isEmpty) {
      return;
    }

    final incoming = _applyInboundMask(data);

    final merged = Uint8List(_pendingBytes.length + incoming.length);
    merged.setRange(0, _pendingBytes.length, _pendingBytes);
    merged.setRange(_pendingBytes.length, merged.length, incoming);
    _pendingBytes = merged;

    while (_pendingBytes.length >= ProtocolConstants.headerSize) {
      final payloadLength = (_pendingBytes[17] << 24) |
          (_pendingBytes[18] << 16) |
          (_pendingBytes[19] << 8) |
          _pendingBytes[20];

      if (payloadLength < 0 || payloadLength > ProtocolConstants.maxPayloadSize) {
        AegisLogger.error('Error parsing message', 'Invalid payload length: $payloadLength');
        _pendingBytes = Uint8List(0);
        return;
      }

      final frameSize = ProtocolConstants.headerSize + payloadLength + ProtocolConstants.macSize;
      if (_pendingBytes.length < frameSize) {
        break;
      }

      try {
        final frame = Uint8List.fromList(_pendingBytes.sublist(0, frameSize));
        final message = MessageEncoder.decode(frame);
        AegisLogger.debug('Received message: ${message.type} (seq: ${message.sequenceId})');
        if (!_messageController.isClosed) {
          _messageController.add(message);
        }
      } catch (e) {
        AegisLogger.error('Error parsing message', e);
        // Skip this frame and continue; do not reset the buffer
      }

      if (_pendingBytes.length == frameSize) {
        _pendingBytes = Uint8List(0);
      } else {
        _pendingBytes = Uint8List.fromList(_pendingBytes.sublist(frameSize));
      }
    }
  }

  Uint8List _applyInboundMask(Uint8List data) {
    if (_transportMaskingKey.isEmpty) {
      return data;
    }

    final masked = Uint8List.fromList(data);
    for (var i = 0; i < masked.length; i++) {
      final keyIndex = (_inboundMaskOffset + i) % _transportMaskingKey.length;
      masked[i] = masked[i] ^ _transportMaskingKey[keyIndex];
    }

    _inboundMaskOffset += masked.length;
    return masked;
  }

  Uint8List _applyOutboundMask(Uint8List data) {
    if (_transportMaskingKey.isEmpty) {
      return data;
    }

    final masked = Uint8List.fromList(data);
    for (var i = 0; i < masked.length; i++) {
      final keyIndex = (_outboundMaskOffset + i) % _transportMaskingKey.length;
      masked[i] = masked[i] ^ _transportMaskingKey[keyIndex];
    }

    _outboundMaskOffset += masked.length;
    return masked;
  }

  /// Cleanup resources
  void dispose() {
    if (_isConnected) {
      disconnect().ignore(); // best-effort close; errors are swallowed
    }
    if (!_messageController.isClosed) _messageController.close();
    if (!_disconnectController.isClosed) _disconnectController.close();
  }
}
