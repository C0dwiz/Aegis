import 'dart:async';

import 'message.dart';
import 'message_payloads.dart';
import 'message_type.dart';

/// Unified dispatcher that splits raw protocol messages into typed streams.
class AegisEventDispatcher {
  late final StreamSubscription<Message> _subscription;

  final StreamController<Message> _ackController =
      StreamController<Message>.broadcast();
  final StreamController<Message> _errorController =
      StreamController<Message>.broadcast();
  final StreamController<PrivateChatMessageEvent> _privateEventController =
      StreamController<PrivateChatMessageEvent>.broadcast();
  final StreamController<ChannelMessageEvent> _channelEventController =
      StreamController<ChannelMessageEvent>.broadcast();
    final StreamController<MessageStatusEvent> _messageStatusController =
      StreamController<MessageStatusEvent>.broadcast();
  final StreamController<ChatListResponse> _chatListController =
      StreamController<ChatListResponse>.broadcast();
  final StreamController<PrivateChatHistoryResponse> _privateHistoryController =
      StreamController<PrivateChatHistoryResponse>.broadcast();
  final StreamController<ChannelHistoryResponse> _channelHistoryController =
      StreamController<ChannelHistoryResponse>.broadcast();

  AegisEventDispatcher(Stream<Message> source) {
    _subscription = source.listen(_route);
  }

  Stream<Message> get ackMessages => _ackController.stream;
  Stream<Message> get errorMessages => _errorController.stream;
  Stream<PrivateChatMessageEvent> get privateMessageEvents =>
      _privateEventController.stream;
  Stream<ChannelMessageEvent> get channelMessageEvents =>
      _channelEventController.stream;
    Stream<MessageStatusEvent> get messageStatusEvents =>
      _messageStatusController.stream;
  Stream<ChatListResponse> get chatListResponses => _chatListController.stream;
  Stream<PrivateChatHistoryResponse> get privateHistoryResponses =>
      _privateHistoryController.stream;
  Stream<ChannelHistoryResponse> get channelHistoryResponses =>
      _channelHistoryController.stream;

  StreamSubscription<Message> onAck(void Function(Message message) handler) {
    return ackMessages.listen(handler);
  }

  StreamSubscription<Message> onError(void Function(Message message) handler) {
    return errorMessages.listen(handler);
  }

  StreamSubscription<PrivateChatMessageEvent> onPrivateMessageEvent(
    void Function(PrivateChatMessageEvent event) handler,
  ) {
    return privateMessageEvents.listen(handler);
  }

  StreamSubscription<ChannelMessageEvent> onChannelMessageEvent(
    void Function(ChannelMessageEvent event) handler,
  ) {
    return channelMessageEvents.listen(handler);
  }

  StreamSubscription<MessageStatusEvent> onMessageStatusEvent(
    void Function(MessageStatusEvent event) handler,
  ) {
    return messageStatusEvents.listen(handler);
  }

  StreamSubscription<PrivateChatHistoryResponse> onPrivateHistoryResponse(
    void Function(PrivateChatHistoryResponse response) handler,
  ) {
    return privateHistoryResponses.listen(handler);
  }

  StreamSubscription<ChannelHistoryResponse> onChannelHistoryResponse(
    void Function(ChannelHistoryResponse response) handler,
  ) {
    return channelHistoryResponses.listen(handler);
  }

  StreamSubscription<ChatListResponse> onChatListResponse(
    void Function(ChatListResponse response) handler,
  ) {
    return chatListResponses.listen(handler);
  }

  Future<void> dispose() async {
    await _subscription.cancel();
    await _ackController.close();
    await _errorController.close();
    await _privateEventController.close();
    await _channelEventController.close();
    await _messageStatusController.close();
    await _chatListController.close();
    await _privateHistoryController.close();
    await _channelHistoryController.close();
  }

  void _route(Message message) {
    switch (message.type) {
      case MessageType.ack:
        _ackController.add(message);
        break;
      case MessageType.error:
        _errorController.add(message);
        break;
      case MessageType.privateChatMessageEvent:
        _tryEmit(
          () => PrivateChatMessageEvent.fromBytes(message.payload),
          _privateEventController,
        );
        break;
      case MessageType.channelMessageEvent:
        _tryEmit(
          () => ChannelMessageEvent.fromBytes(message.payload),
          _channelEventController,
        );
        break;
      case MessageType.messageStatusEvent:
        _tryEmit(
          () => MessageStatusEvent.fromBytes(message.payload),
          _messageStatusController,
        );
        break;
      case MessageType.chatListResponse:
        _tryEmit(
          () => ChatListResponse.fromBytes(message.payload),
          _chatListController,
        );
        break;
      case MessageType.privateChatHistoryResponse:
        _tryEmit(
          () => PrivateChatHistoryResponse.fromBytes(message.payload),
          _privateHistoryController,
        );
        break;
      case MessageType.channelHistoryResponse:
        _tryEmit(
          () => ChannelHistoryResponse.fromBytes(message.payload),
          _channelHistoryController,
        );
        break;
      default:
        break;
    }
  }

  void _tryEmit<T>(T Function() parse, StreamController<T> controller) {
    try {
      controller.add(parse());
    } catch (_) {
      // Ignore payload parse errors so dispatcher never breaks message flow.
    }
  }
}
