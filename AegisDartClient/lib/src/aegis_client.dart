import 'dart:async';
import 'dart:convert';
import 'dart:math';
import 'dart:typed_data';
import 'package:msgpack_dart/msgpack_dart.dart' as msgpack;
import 'message.dart';
import 'message_type.dart';
import 'message_payloads.dart';
import 'event_dispatcher.dart';
import 'transport.dart';
import 'exceptions.dart';
import 'protocol_constants.dart';

extension _ChannelMessageResponseCompat on ChannelMessageResponse {
  MediaSendResponse toMediaSendResponse() => MediaSendResponse(
    success: success,
    messageId: messageId,
    messageText: messageText,
  );
}

extension _GroupMessageResponseCompat on GroupMessageSendResponse {
  MediaSendResponse toMediaSendResponse() => MediaSendResponse(
    success: success,
    messageId: messageId,
    messageText: message,
  );
}

extension _PrivateMessageResponseCompat on PrivateChatMessageResponse {
  MediaSendResponse toMediaSendResponse() => MediaSendResponse(
    success: success,
    messageId: messageId,
    messageText: messageText,
  );
}

extension _MediaSendResponseCompat on MediaSendResponse {
  PrivateChatMessageResponse toPrivateLike() => PrivateChatMessageResponse(
    success: success,
    messageId: messageId,
    messageText: messageText,
  );

  ChannelMessageResponse toChannelLike() => ChannelMessageResponse(
    success: success,
    messageId: messageId,
    messageText: messageText,
  );
}

/// Main Aegis client class
class AegisClient {
  late AegisTransport _transport;
  bool _isAuthenticated = false;
  int? _userId;
  String? _username;
  late final AegisEventDispatcher events;

  // Per-client sequence-ID counter so responses can be matched unambiguously.
  int _nextSeqId = 1;

  /// Stream of incoming messages (unsolicited pushes from the server)
  Stream<Message> get messages => _transport.messages;

  /// Typed stream of incoming private message events.
  Stream<PrivateChatMessageEvent> get privateMessageEvents =>
      events.privateMessageEvents;

  /// Typed stream of incoming channel message events.
  Stream<ChannelMessageEvent> get channelMessageEvents =>
      events.channelMessageEvents;

    /// Typed stream of incoming async delivery/read status events.
    Stream<MessageStatusEvent> get messageStatusEvents =>
      events.messageStatusEvents;

  /// Stream of disconnect events
  Stream<void> get disconnects => _transport.disconnects;

  /// Whether this client is currently connected
  bool get isConnected => _transport.isConnected;

  /// Whether this client has completed authentication
  bool get isAuthenticated => _isAuthenticated;

  /// The authenticated user's ID, available after [login] or [loginWithToken]
  int? get userId => _userId;

  /// The authenticated user's username, available after [login] or [loginWithToken]
  String? get username => _username;

  /// Create a new Aegis client
  AegisClient() {
    _transport = AegisTransport();
    events = AegisEventDispatcher(_transport.messages);
  }

  // ─── Connection ────────────────────────────────────────────────────────────

  /// Connect to the Aegis server and complete the protocol handshake.
  Future<void> connect(
    String host,
    int port, {
    Duration? timeout,
    String? transportMaskingKey,
    bool enableMaskingAutoFallback = true,
  }) async {
    final hasMaskingKey = transportMaskingKey != null && transportMaskingKey.trim().isNotEmpty;

    if (!hasMaskingKey || !enableMaskingAutoFallback) {
      await _transport.connect(
        host,
        port,
        timeout: timeout,
        transportMaskingKey: transportMaskingKey,
      );
      await _sendHandshake();
      return;
    }

    try {
      await _transport.connect(
        host,
        port,
        timeout: timeout,
        transportMaskingKey: transportMaskingKey,
      );
      await _sendHandshake();
    } catch (firstError) {
      await _transport.disconnect();

      try {
        await _transport.connect(
          host,
          port,
          timeout: timeout,
        );
        await _sendHandshake();
      } catch (secondError) {
        throw Exception(
          'Failed connect with masking and fallback. maskedError: $firstError; plainError: $secondError',
        );
      }
    }
  }

  /// Disconnect from the server.
  Future<void> disconnect() async {
    if (_transport.isConnected && _isAuthenticated) {
      await _publishPresence(isOnline: false);
    }

    await _transport.disconnect();
    _isAuthenticated = false;
    _userId = null;
    _username = null;
  }

  /// Release all resources.
  void dispose() {
    events.dispose().ignore();
    _transport.dispose();
  }

  // ─── Authentication ─────────────────────────────────────────────────────────

  /// Authenticate with username and password.
  ///
  /// Throws [NotConnectedException] if not connected.
  /// Throws an [Exception] if authentication fails.
  Future<void> login(String username, String password,
      {String clientInfo = 'aegis-dart-client'}) async {
    _requireConnected();
    final payload = msgpack.serialize({
      'Username': username,
      'Password': password,
      'ClientInfo': clientInfo,
    });
    await _doAuthenticate(payload);
  }

  /// Re-authenticate with a previously issued session token.
  Future<void> loginWithToken(String token) async {
    _requireConnected();
    final payload = msgpack.serialize({
      'Token': token,
      'ClientInfo': 'aegis-dart-client',
    });
    await _doAuthenticate(payload);
  }

  /// Low-level authenticate: accepts either a raw JSON string or a token.
  ///
  /// Prefer [login] / [loginWithToken] for clarity.
  Future<void> authenticate(dynamic authPayloadOrToken) async {
    _requireConnected();
    List<int> payload;
    if (authPayloadOrToken is List<int>) {
      payload = authPayloadOrToken;
    } else if (authPayloadOrToken is String && authPayloadOrToken.trim().startsWith('{')) {
      payload = msgpack.serialize(jsonDecode(authPayloadOrToken));
    } else {
      payload = msgpack.serialize({
        'Token': authPayloadOrToken,
        'ClientInfo': 'aegis-dart-client',
      });
    }
    await _doAuthenticate(payload);
  }

  Future<void> _doAuthenticate(List<int> payload) async {
    final msg = Message.withType(MessageType.auth, payload);
    final response = await _sendAndWaitResponse(msg);

    final decoded = msgpack.deserialize(response.payload);
    if (decoded == null || decoded['Success'] != true) {
      throw Exception('Authentication failed');
    }

    _isAuthenticated = true;
    _userId = decoded['UserId'] as int?;
    _username = decoded['Username'] as String?;

    await _publishPresence(isOnline: true);
  }

  // ─── Registration ───────────────────────────────────────────────────────────

  /// Register a new account on the server.
  Future<RegistrationResponse> register(
      String username, String email, String password, String publicKey) async {
    _requireConnected();

    final request = RegistrationRequest(
      username: username,
      email: email,
      password: password,
      publicKey: publicKey,
    );

    final msg = Message.withType(MessageType.register, request.toBytes());
    final response = await _sendAndWaitResponse(msg,
        expectedTypes: {MessageType.registerResponse});
    return RegistrationResponse.fromBytes(response.payload);
  }

  // ─── Messaging ──────────────────────────────────────────────────────────────

  /// Send a direct message using the legacy binary-free JSON format (type 3).
  ///
  /// For proper private messaging prefer [sendPrivateMessage].
  Future<void> sendMessage(
    String content, {
    int toUserId = 0,
    ParseMode? parseMode,
  }) async {
    _requireAuthenticated();
    final payloadBytes = utf8.encode(jsonEncode({
      'RecipientId': toUserId,
      'Content': content,
      if (parseMode != null) 'ParseMode': parseMode.value,
    }));
    final msg = Message.withType(MessageType.message, payloadBytes);
    msg.sequenceId = _nextSeqId++;
    await _transport.sendMessage(msg);
  }

  /// Send a plain text message to a group.
  Future<MediaSendResponse> sendGroupMessage(
    int groupId,
    String content, {
    MessageContentType contentType = MessageContentType.text,
    int? replyToMessageId,
    ParseMode? parseMode,
  }) async {
    _requireAuthenticated();

    final request = GroupMessageSendRequest(
      groupId: groupId,
      content: content,
      contentType: contentType,
      replyToMessageId: replyToMessageId,
      parseMode: parseMode?.value,
    );

    final msg = Message.withType(
      MessageType.groupMessageSend,
      request.toBytes(),
    );
    final response = await _sendAndWaitResponse(
      msg,
      expectedTypes: {MessageType.groupMessageResponse, MessageType.ack},
    );
    return GroupMessageSendResponse.fromBytes(response.payload)
        .toMediaSendResponse();
  }

  /// Send a Markdown-formatted message to a group.
  Future<MediaSendResponse> sendGroupMarkdown(
    int groupId,
    String markdownText, {
    int? replyToMessageId,
  }) {
    return sendGroupMessage(
      groupId,
      markdownText,
      contentType: MessageContentType.text,
      replyToMessageId: replyToMessageId,
      parseMode: ParseMode.markdown,
    );
  }

  /// Send a private chat message to another user (type 17).
  Future<PrivateChatMessageResponse> sendPrivateMessage(
      int toUserId, String content,
      {
      MessageContentType contentType = MessageContentType.text,
      ParseMode? parseMode,
    }) async {
    _requireAuthenticated();

    final request = PrivateChatMessageRequest(
      toUserId: toUserId,
      content: content,
      contentType: contentType,
      parseMode: parseMode?.value,
    );

    final msg =
        Message.withType(MessageType.privateChatMessage, request.toBytes());
    final response = await _sendAndWaitResponse(msg,
        expectedTypes: {MessageType.privateChatMessage, MessageType.ack});
    return PrivateChatMessageResponse.fromBytes(response.payload);
  }

  /// Send a photo to a private chat.
  Future<PrivateChatMessageResponse> sendPrivatePhoto(
    int toUserId,
    Uint8List photoBytes, {
    String? caption,
    String fileName = 'photo.jpg',
    String mimeType = 'image/jpeg',
  }) async {
    final response = await sendMedia(
      chatType: ChatTargetType.private,
      chatId: toUserId,
      mediaBytes: photoBytes,
      mediaKind: MediaKind.photo,
      caption: caption,
      fileName: fileName,
      mimeType: mimeType,
    );
    return response.toPrivateLike();
  }

  /// Send a file to a private chat.
  Future<PrivateChatMessageResponse> sendPrivateFile(
    int toUserId,
    Uint8List fileBytes, {
    String? caption,
    required String fileName,
    String mimeType = 'application/octet-stream',
  }) async {
    final response = await sendMedia(
      chatType: ChatTargetType.private,
      chatId: toUserId,
      mediaBytes: fileBytes,
      mediaKind: MediaKind.file,
      caption: caption,
      fileName: fileName,
      mimeType: mimeType,
    );
    return response.toPrivateLike();
  }

  /// Send a voice message to a private chat.
  Future<PrivateChatMessageResponse> sendPrivateVoice(
    int toUserId,
    Uint8List voiceBytes, {
    String? caption,
    String fileName = 'voice.ogg',
    String mimeType = 'audio/ogg',
  }) async {
    final response = await sendMedia(
      chatType: ChatTargetType.private,
      chatId: toUserId,
      mediaBytes: voiceBytes,
      mediaKind: MediaKind.voice,
      caption: caption,
      fileName: fileName,
      mimeType: mimeType,
    );
    return response.toPrivateLike();
  }

  /// Get all chats for the authenticated user.
  Future<ChatListResponse> getChatList() async {
    _requireAuthenticated();

    final request = ChatListRequest();
    final msg = Message.withType(MessageType.chatListRequest, request.toBytes());
    final response = await _sendAndWaitResponse(msg,
        expectedTypes: {MessageType.chatListResponse});
    return ChatListResponse.fromBytes(response.payload);
  }

  /// Get private chat history with a peer.
  Future<PrivateChatHistoryResponse> getPrivateHistory(
    int peerUserId, {
    int limit = 100,
    int? beforeMessageId,
  }) async {
    _requireAuthenticated();

    final request = PrivateChatHistoryRequest(
      peerUserId: peerUserId,
      limit: limit,
      beforeMessageId: beforeMessageId,
    );

    final msg = Message.withType(
      MessageType.privateChatHistoryRequest,
      request.toBytes(),
    );
    final response = await _sendAndWaitResponse(msg,
        expectedTypes: {MessageType.privateChatHistoryResponse});
    return PrivateChatHistoryResponse.fromBytes(response.payload);
  }

  /// Get channel history.
  Future<ChannelHistoryResponse> getChannelHistory(
    int channelId, {
    int limit = 100,
    int? beforeMessageId,
  }) async {
    _requireAuthenticated();

    final request = ChannelHistoryRequest(
      channelId: channelId,
      limit: limit,
      beforeMessageId: beforeMessageId,
    );

    final msg = Message.withType(
      MessageType.channelHistoryRequest,
      request.toBytes(),
    );
    final response = await _sendAndWaitResponse(msg,
        expectedTypes: {MessageType.channelHistoryResponse});
    return ChannelHistoryResponse.fromBytes(response.payload);
  }

  /// Register a callback for private message events.
  StreamSubscription<PrivateChatMessageEvent> onPrivateMessageEvent(
    void Function(PrivateChatMessageEvent event) handler,
  ) {
    return privateMessageEvents.listen(handler);
  }

  /// Register a callback for channel message events.
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

  // ─── Channels ───────────────────────────────────────────────────────────────

  /// Send a message to a channel.
  Future<ChannelMessageResponse> sendChannelMessage(
    int channelId,
    String content, {
    MessageContentType contentType = MessageContentType.text,
    int? replyToMessageId,
    ParseMode? parseMode,
  }) async {
    _requireAuthenticated();

    final request = ChannelMessageRequest(
      channelId: channelId,
      content: content,
      contentType: contentType,
      replyToMessageId: replyToMessageId,
      parseMode: parseMode?.value,
    );

    final msg =
        Message.withType(MessageType.channelMessage, request.toBytes());
    final response = await _sendAndWaitResponse(msg,
        expectedTypes: {MessageType.channelMessage, MessageType.ack});
    return ChannelMessageResponse.fromBytes(response.payload);
  }

  /// Send a photo to a channel.
  Future<ChannelMessageResponse> sendChannelPhoto(
    int channelId,
    Uint8List photoBytes, {
    String? caption,
    String fileName = 'photo.jpg',
    String mimeType = 'image/jpeg',
    int? replyToMessageId,
  }) async {
    final response = await sendMedia(
      chatType: ChatTargetType.channel,
      chatId: channelId,
      mediaBytes: photoBytes,
      mediaKind: MediaKind.photo,
      caption: caption,
      fileName: fileName,
      mimeType: mimeType,
      replyToMessageId: replyToMessageId,
    );
    return response.toChannelLike();
  }

  /// Send a file to a channel.
  Future<ChannelMessageResponse> sendChannelFile(
    int channelId,
    Uint8List fileBytes, {
    String? caption,
    required String fileName,
    String mimeType = 'application/octet-stream',
    int? replyToMessageId,
  }) async {
    final response = await sendMedia(
      chatType: ChatTargetType.channel,
      chatId: channelId,
      mediaBytes: fileBytes,
      mediaKind: MediaKind.file,
      caption: caption,
      fileName: fileName,
      mimeType: mimeType,
      replyToMessageId: replyToMessageId,
    );
    return response.toChannelLike();
  }

  /// Send a voice message to a channel.
  Future<ChannelMessageResponse> sendChannelVoice(
    int channelId,
    Uint8List voiceBytes, {
    String? caption,
    String fileName = 'voice.ogg',
    String mimeType = 'audio/ogg',
    int? replyToMessageId,
  }) async {
    final response = await sendMedia(
      chatType: ChatTargetType.channel,
      chatId: channelId,
      mediaBytes: voiceBytes,
      mediaKind: MediaKind.voice,
      caption: caption,
      fileName: fileName,
      mimeType: mimeType,
      replyToMessageId: replyToMessageId,
    );
    return response.toChannelLike();
  }

  /// Unified media sending for private chats, channels and groups.
  ///
  /// `chatType`:
  /// - [ChatTargetType.private] -> `chatId` is `toUserId`
  /// - [ChatTargetType.channel] -> `chatId` is `channelId`
  /// - [ChatTargetType.group] -> `chatId` is `groupId`
  Future<MediaSendResponse> sendMedia({
    required ChatTargetType chatType,
    required int chatId,
    required Uint8List mediaBytes,
    required MediaKind mediaKind,
    String? caption,
    ParseMode? parseMode,
    String? fileName,
    String? mimeType,
    int? replyToMessageId,
  }) async {
    final resolvedFileName = fileName ??
        switch (mediaKind) {
          MediaKind.photo => 'photo.jpg',
          MediaKind.video => 'video.mp4',
          MediaKind.gif => 'animation.gif',
          MediaKind.file => 'file.bin',
          MediaKind.voice => 'voice.ogg',
        };
    final resolvedMime = mimeType ??
        switch (mediaKind) {
          MediaKind.photo => 'image/jpeg',
          MediaKind.video => 'video/mp4',
          MediaKind.gif => 'image/gif',
          MediaKind.file => 'application/octet-stream',
          MediaKind.voice => 'audio/ogg',
        };
    final contentType = switch (mediaKind) {
      MediaKind.photo => MessageContentType.image,
      MediaKind.video => MessageContentType.video,
      MediaKind.gif => MessageContentType.image,
      MediaKind.file => MessageContentType.file,
      MediaKind.voice => MessageContentType.audio,
    };

    final attachment = MediaAttachmentPayload(
      fileName: resolvedFileName,
      mimeType: resolvedMime,
      base64Data: base64Encode(mediaBytes),
      sizeBytes: mediaBytes.length,
    );

    return sendMediaBatch(
      chatType: chatType,
      chatId: chatId,
      attachments: [attachment],
      caption: caption,
      parseMode: parseMode,
      replyToMessageId: replyToMessageId,
      forcedContentType: contentType,
    );
  }

  /// Send up to 10 mixed attachments in a single message (images/files/audio/video/etc).
  Future<MediaSendResponse> sendMediaBatch({
    required ChatTargetType chatType,
    required int chatId,
    required List<MediaAttachmentPayload> attachments,
    String? caption,
    ParseMode? parseMode,
    int? replyToMessageId,
    MessageContentType? forcedContentType,
  }) async {
    _requireAuthenticated();

    if (attachments.isEmpty) {
      throw ArgumentError('attachments must not be empty');
    }

    if (attachments.length > 10) {
      throw ArgumentError('A maximum of 10 attachments is allowed per message');
    }

    final contentType = forcedContentType ?? _resolveBatchContentType(attachments);

    switch (chatType) {
      case ChatTargetType.private:
        final request = PrivateChatMessageRequest(
          toUserId: chatId,
          content: caption,
          contentType: contentType,
          attachment: attachments.first,
          attachments: attachments,
          parseMode: parseMode?.value,
        );
        final msg = Message.withType(
          MessageType.privateChatMessage,
          request.toBytes(),
        );
        final response = await _sendAndWaitResponse(
          msg,
          expectedTypes: {MessageType.privateChatMessage, MessageType.ack},
        );
        return PrivateChatMessageResponse.fromBytes(response.payload)
          .toMediaSendResponse();

      case ChatTargetType.channel:
        final request = ChannelMessageRequest(
          channelId: chatId,
          content: caption,
          contentType: contentType,
          replyToMessageId: replyToMessageId,
          attachment: attachments.first,
          attachments: attachments,
          parseMode: parseMode?.value,
        );
        final msg = Message.withType(
          MessageType.channelMessage,
          request.toBytes(),
        );
        final response = await _sendAndWaitResponse(
          msg,
          expectedTypes: {MessageType.channelMessage, MessageType.ack},
        );
        return ChannelMessageResponse.fromBytes(response.payload)
          .toMediaSendResponse();

      case ChatTargetType.group:
        final request = GroupMessageSendRequest(
          groupId: chatId,
          content: caption,
          contentType: contentType,
          replyToMessageId: replyToMessageId,
          attachment: attachments.first,
          attachments: attachments,
          parseMode: parseMode?.value,
        );
        final msg = Message.withType(
          MessageType.groupMessageSend,
          request.toBytes(),
        );
        final response = await _sendAndWaitResponse(
          msg,
          expectedTypes: {MessageType.groupMessageResponse, MessageType.ack},
        );
        return GroupMessageSendResponse.fromBytes(response.payload)
          .toMediaSendResponse();
    }
  }

  MessageContentType _resolveBatchContentType(List<MediaAttachmentPayload> attachments) {
    final mimes = attachments.map((item) => item.mimeType.toLowerCase()).toList(growable: false);

    if (mimes.every((mime) => mime.startsWith('image/'))) {
      return MessageContentType.image;
    }

    if (mimes.every((mime) => mime.startsWith('video/'))) {
      return MessageContentType.video;
    }

    if (mimes.every((mime) => mime.startsWith('audio/'))) {
      return MessageContentType.audio;
    }

    return MessageContentType.file;
  }

  /// Unified file sending helper built on top of [sendMedia].
  Future<MediaSendResponse> sendFile({
    required ChatTargetType chatType,
    required int chatId,
    required Uint8List fileBytes,
    required String fileName,
    String mimeType = 'application/octet-stream',
    String? caption,
    ParseMode? parseMode,
    int? replyToMessageId,
  }) {
    return sendMedia(
      chatType: chatType,
      chatId: chatId,
      mediaBytes: fileBytes,
      mediaKind: MediaKind.file,
      fileName: fileName,
      mimeType: mimeType,
      caption: caption,
      parseMode: parseMode,
      replyToMessageId: replyToMessageId,
    );
  }

  /// Unified voice message helper built on top of [sendMedia].
  Future<MediaSendResponse> sendVoiceMessage({
    required ChatTargetType chatType,
    required int chatId,
    required Uint8List voiceBytes,
    String fileName = 'voice.ogg',
    String mimeType = 'audio/ogg',
    String? caption,
    ParseMode? parseMode,
    int? replyToMessageId,
  }) {
    return sendMedia(
      chatType: chatType,
      chatId: chatId,
      mediaBytes: voiceBytes,
      mediaKind: MediaKind.voice,
      fileName: fileName,
      mimeType: mimeType,
      caption: caption,
      parseMode: parseMode,
      replyToMessageId: replyToMessageId,
    );
  }

  /// Convenience helper for Markdown-formatted private text messages.
  Future<PrivateChatMessageResponse> sendPrivateMarkdown(
    int toUserId,
    String markdownText,
  ) {
    return sendPrivateMessage(
      toUserId,
      markdownText,
      contentType: MessageContentType.text,
      parseMode: ParseMode.markdown,
    );
  }

  /// Convenience helper for Markdown-formatted channel text messages.
  Future<ChannelMessageResponse> sendChannelMarkdown(
    int channelId,
    String markdownText, {
    int? replyToMessageId,
  }) {
    return sendChannelMessage(
      channelId,
      markdownText,
      contentType: MessageContentType.text,
      replyToMessageId: replyToMessageId,
      parseMode: ParseMode.markdown,
    );
  }

  /// Create a new channel.
  Future<ChannelCreateResponse> createChannel(
    String name, {
    String? description,
    ChannelType type = ChannelType.public,
  }) async {
    _requireAuthenticated();

    final request = ChannelCreateRequest(
      name: name,
      description: description,
      type: type,
    );

    final msg =
        Message.withType(MessageType.channelCreate, request.toBytes());
    final response = await _sendAndWaitResponse(msg,
        expectedTypes: {MessageType.channelCreate, MessageType.ack});
    return ChannelCreateResponse.fromBytes(response.payload);
  }

  /// Join an existing public channel.
  Future<ChannelJoinResponse> joinChannel(int channelId) async {
    _requireAuthenticated();

    final request = ChannelJoinRequest(channelId: channelId);
    final msg = Message.withType(MessageType.channelJoin, request.toBytes());
    final response = await _sendAndWaitResponse(msg,
        expectedTypes: {MessageType.channelJoin, MessageType.ack});
    return ChannelJoinResponse.fromBytes(response.payload);
  }

  /// Edit channel properties (name, description, avatar URL).
  Future<ChannelEditResponse> updateChannel(
    int channelId, {
    String? name,
    String? description,
    String? avatarUrl,
  }) async {
    _requireAuthenticated();

    final request = ChannelEditRequest(
      channelId: channelId,
      name: name,
      description: description,
      avatarUrl: avatarUrl,
    );

    final msg = Message.withType(MessageType.channelEdit, request.toBytes());
    final response = await _sendAndWaitResponse(msg,
        expectedTypes: {MessageType.channelEditResponse, MessageType.ack});
    return ChannelEditResponse.fromBytes(response.payload);
  }

  /// Upload a channel avatar from raw image bytes.
  ///
  /// The bytes are base64-encoded into a data URL and stored as the avatar.
  /// [mimeType] defaults to `'image/jpeg'`.
  Future<ChannelEditResponse> uploadChannelAvatar(
    int channelId,
    Uint8List imageBytes, {
    String mimeType = 'image/jpeg',
  }) async {
    final dataUrl = 'data:$mimeType;base64,${base64Encode(imageBytes)}';
    return updateChannel(channelId, avatarUrl: dataUrl);
  }

  // ─── Groups (group chats) ─────────────────────────────────────────────────────

  /// Edit group chat properties (name, description, avatar URL).
  Future<GroupEditResponse> updateGroup(
    int groupId, {
    String? name,
    String? description,
    String? avatarUrl,
  }) async {
    _requireAuthenticated();

    final request = GroupEditRequest(
      groupId: groupId,
      name: name,
      description: description,
      avatarUrl: avatarUrl,
    );

    final msg = Message.withType(MessageType.groupEdit, request.toBytes());
    final response = await _sendAndWaitResponse(msg,
        expectedTypes: {MessageType.groupEditResponse, MessageType.ack});
    return GroupEditResponse.fromBytes(response.payload);
  }

  /// Upload a group chat avatar from raw image bytes.
  ///
  /// The bytes are base64-encoded into a data URL and stored as the avatar.
  /// [mimeType] defaults to `'image/jpeg'`.
  Future<GroupEditResponse> uploadGroupAvatar(
    int groupId,
    Uint8List imageBytes, {
    String mimeType = 'image/jpeg',
  }) async {
    final dataUrl = 'data:$mimeType;base64,${base64Encode(imageBytes)}';
    return updateGroup(groupId, avatarUrl: dataUrl);
  }

  // ─── Profile ──────────────────────────────────────────────────────────────────

  /// Get the authenticated user's own profile.
  Future<ProfileGetResponse> getOwnProfile() async {
    _requireAuthenticated();
    final request = ProfileGetRequest();
    final msg = Message.withType(MessageType.profileGet, request.toBytes());
    final response = await _sendAndWaitResponse(msg,
        expectedTypes: {MessageType.profileGetResponse});
    return ProfileGetResponse.fromBytes(response.payload);
  }

  /// Get another user's profile by ID or username.
  Future<ProfileGetResponse> getProfile({int? userId, String? username}) async {
    _requireAuthenticated();
    final request = ProfileGetRequest(userId: userId, username: username);
    final msg = Message.withType(MessageType.profileGet, request.toBytes());
    final response = await _sendAndWaitResponse(msg,
        expectedTypes: {MessageType.profileGetResponse});
    return ProfileGetResponse.fromBytes(response.payload);
  }

  /// Update the authenticated user's profile fields.
  Future<ProfileUpdateResponse> updateProfile({
    String? displayName,
    String? avatarUrl,
    String? bio,
    String? username,
  }) async {
    _requireAuthenticated();

    final request = ProfileUpdateRequest(
      displayName: displayName,
      avatarUrl: avatarUrl,
      bio: bio,
      username: username,
    );

    final msg = Message.withType(MessageType.profileUpdate, request.toBytes());
    final response = await _sendAndWaitResponse(msg,
        expectedTypes: {MessageType.profileUpdateResponse});
    return ProfileUpdateResponse.fromBytes(response.payload);
  }

  /// Upload a user avatar from raw image bytes.
  ///
  /// The bytes are base64-encoded into a data URL and stored as the avatar.
  /// [mimeType] defaults to `'image/jpeg'`.
  Future<ProfileUpdateResponse> uploadUserAvatar(
    Uint8List imageBytes, {
    String mimeType = 'image/jpeg',
  }) async {
    final dataUrl = 'data:$mimeType;base64,${base64Encode(imageBytes)}';
    final result = await addProfileAvatar(dataUrl, makePrimary: true);
    return ProfileUpdateResponse(
      success: result.success,
      message: result.message,
      profile: null,
    );
  }

  Future<ProfileAvatarMutationResponse> addProfileAvatar(
    String avatarUrl, {
    bool makePrimary = false,
  }) async {
    _requireAuthenticated();
    final request = ProfileAvatarAddRequest(
      avatarUrl: avatarUrl,
      makePrimary: makePrimary,
    );
    final msg = Message.withType(MessageType.profileAvatarAdd, request.toBytes());
    final response = await _sendAndWaitResponse(msg,
        expectedTypes: {MessageType.profileAvatarAddResponse});
    return ProfileAvatarMutationResponse.fromBytes(response.payload);
  }

  Future<ProfileAvatarListResponse> listProfileAvatars() async {
    _requireAuthenticated();
    final msg = Message.withType(
      MessageType.profileAvatarList,
      utf8.encode('{}'),
    );
    final response = await _sendAndWaitResponse(msg,
        expectedTypes: {MessageType.profileAvatarListResponse});
    return ProfileAvatarListResponse.fromBytes(response.payload);
  }

  Future<ProfileAvatarMutationResponse> deleteProfileAvatar(int avatarId) async {
    _requireAuthenticated();
    final request = ProfileAvatarDeleteRequest(avatarId: avatarId);
    final msg = Message.withType(MessageType.profileAvatarDelete, request.toBytes());
    final response = await _sendAndWaitResponse(msg,
        expectedTypes: {MessageType.profileAvatarDeleteResponse});
    return ProfileAvatarMutationResponse.fromBytes(response.payload);
  }

  Future<ProfileAvatarMutationResponse> setPrimaryProfileAvatar(int avatarId) async {
    _requireAuthenticated();
    final request = ProfileAvatarSetPrimaryRequest(avatarId: avatarId);
    final msg = Message.withType(MessageType.profileAvatarSetPrimary, request.toBytes());
    final response = await _sendAndWaitResponse(msg,
        expectedTypes: {MessageType.profileAvatarSetPrimaryResponse});
    return ProfileAvatarMutationResponse.fromBytes(response.payload);
  }

  Future<ChannelLinkResponse> updateChannelLinks(
    int channelId, {
    String? publicAlias,
    bool regeneratePrivateInvite = false,
  }) async {
    _requireAuthenticated();
    final request = ChannelLinkUpdateRequest(
      channelId: channelId,
      publicAlias: publicAlias,
      regeneratePrivateInvite: regeneratePrivateInvite,
    );
    final msg = Message.withType(MessageType.channelLinkUpdate, request.toBytes());
    final response = await _sendAndWaitResponse(msg,
        expectedTypes: {MessageType.channelLinkUpdateResponse});
    return ChannelLinkResponse.fromBytes(response.payload);
  }

  Future<ChannelLinkResponse> getChannelLinks(int channelId) async {
    _requireAuthenticated();
    final request = ChannelLinkRequest(channelId: channelId);
    final msg = Message.withType(MessageType.channelLinkGet, request.toBytes());
    final response = await _sendAndWaitResponse(msg,
        expectedTypes: {MessageType.channelLinkGetResponse});
    return ChannelLinkResponse.fromBytes(response.payload);
  }

  Future<ChannelResolveResponse> resolveChannelLink(String linkOrAlias) async {
    _requireAuthenticated();
    final request = ChannelResolveRequest(linkOrAlias: linkOrAlias);
    final msg = Message.withType(MessageType.channelResolve, request.toBytes());
    final response = await _sendAndWaitResponse(msg,
        expectedTypes: {MessageType.channelResolveResponse});
    return ChannelResolveResponse.fromBytes(response.payload);
  }

  Future<ChannelJoinResponse> joinChannelByLink(String linkOrAlias) async {
    _requireAuthenticated();
    final request = ChannelResolveRequest(linkOrAlias: linkOrAlias);
    final msg = Message.withType(MessageType.channelJoinByLink, request.toBytes());
    final response = await _sendAndWaitResponse(msg,
        expectedTypes: {MessageType.channelJoinByLinkResponse});
    return ChannelJoinResponse.fromBytes(response.payload);
  }

  // ─── User search ─────────────────────────────────────────────────────────────

  /// Search for users by username prefix.
  Future<UserSearchResponse> searchUsers(String query,
      {int limit = 20}) async {
    _requireAuthenticated();

    final request = UserSearchRequest(query: query, limit: limit);
    final msg = Message.withType(MessageType.userSearch, request.toBytes());
    final response = await _sendAndWaitResponse(msg,
        expectedTypes: {MessageType.userSearchResult});
    return UserSearchResponse.fromBytes(response.payload);
  }

  // ─── Ping ────────────────────────────────────────────────────────────────────

  /// Send a ping to the server (fire-and-forget).
  Future<void> ping() async {
    _requireConnected();
    final timestamp = DateTime.now().millisecondsSinceEpoch;
    final msg = Message.withType(MessageType.ping, _int64ToBytes(timestamp));
    msg.sequenceId = _nextSeqId++;
    await _transport.sendMessage(msg);
  }

  /// Explicitly publish user presence state to the server.
  Future<void> setPresence({required bool isOnline}) async {
    _requireAuthenticated();
    await _publishPresence(isOnline: isOnline);
  }

  // ─── Internal helpers ────────────────────────────────────────────────────────

  /// Assign a sequence ID, subscribe for the matching response, then send.
  ///
  /// Subscribing BEFORE the send prevents a race condition where the server
  /// replies faster than the subscription is established.
  Future<Message> _sendAndWaitResponse(
    Message message, {
    Set<MessageType>? expectedTypes,
    Duration timeout = const Duration(seconds: 10),
  }) async {
    // Assign sequence ID before subscribing/sending
    message.sequenceId = _nextSeqId++;
    message.flags |= ProtocolConstants.flagRequiresAck;

    final seqId = message.sequenceId;

    // Subscribe first (synchronous operation on the broadcast stream)
    final responseFuture = messages
        .firstWhere((msg) {
          if (msg.sequenceId != seqId) return false;
          if (expectedTypes != null && !expectedTypes.contains(msg.type)) {
            return false;
          }
          return true;
        })
        .timeout(timeout, onTimeout: () {
          throw TimeoutException(
              'No response for seq=$seqId', timeout);
        });

    // Now send
    await _transport.sendMessage(message);

    return responseFuture;
  }

  /// Send the initial handshake after connect.
  Future<void> _sendHandshake() async {
    final payload = <int>[];
    payload.addAll(_int32ToBytes(ProtocolConstants.versionMajor * 1000 +
        ProtocolConstants.versionMinor)); // client version
    payload.addAll(_generateNonce()); // 12 cryptographically random bytes

    final msg = Message.withType(MessageType.handshake, payload);
    msg.sequenceId = _nextSeqId++;
    await _transport.sendMessage(msg);
  }

  Future<void> _publishPresence({required bool isOnline}) async {
    try {
      final request = UserPresenceUpdateRequest(
        isOnline: isOnline,
        clientTimestamp: DateTime.now().toUtc(),
      );
      final msg = Message.withType(MessageType.userPresence, request.toBytes());
      msg.sequenceId = _nextSeqId++;
      await _transport.sendMessage(msg);
    } catch (_) {
      // Presence signal is best-effort and must not block auth/disconnect.
    }
  }

  void _requireConnected() {
    if (!_transport.isConnected) throw NotConnectedException();
  }

  void _requireAuthenticated() {
    _requireConnected();
    if (!_isAuthenticated) throw Exception('Not authenticated');
  }

  Map<String, dynamic>? _tryDecodeJson(List<int> payload) {
    if (payload.isEmpty) return null;
    try {
      final decoded = jsonDecode(utf8.decode(payload));
      if (decoded is Map<String, dynamic>) return decoded;
    } catch (_) {}
    return null;
  }

  String? _extractErrorMessage(List<int> payload) {
    final decoded = _tryDecodeJson(payload);
    if (decoded == null) return null;
    return (decoded['Error'] ?? decoded['Message'] ?? decoded['MessageText'])
        as String?;
  }

  /// Generate 12 cryptographically random bytes for the handshake nonce.
  List<int> _generateNonce() {
    final rng = Random.secure();
    return List<int>.generate(12, (_) => rng.nextInt(256));
  }

  List<int> _int64ToBytes(int value) {
    final bytes = ByteData(8)
      ..setUint64(0, value, Endian.big);
    return bytes.buffer.asUint8List().toList();
  }

  List<int> _int32ToBytes(int value) {
    final bytes = ByteData(4)
      ..setUint32(0, value, Endian.big);
    return bytes.buffer.asUint8List().toList();
  }
}
