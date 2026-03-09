import 'dart:convert';

/// Message content types
enum MessageContentType {
  text(0),
  image(1),
  video(2),
  audio(3),
  file(4),
  location(5);

  const MessageContentType(this.value);
  final int value;

  static MessageContentType fromValue(int value) {
    return MessageContentType.values.firstWhere(
      (type) => type.value == value,
      orElse: () => MessageContentType.text,
    );
  }
}

/// Channel types
enum ChannelType {
  public(0),
  private(1),
  group(2);

  const ChannelType(this.value);
  final int value;

  static ChannelType fromValue(int value) {
    return ChannelType.values.firstWhere(
      (type) => type.value == value,
      orElse: () => ChannelType.public,
    );
  }
}

/// Registration request payload
class RegistrationRequest {
  final String username;
  final String email;
  final String password;
  final String publicKey;

  RegistrationRequest({
    required this.username,
    required this.email,
    required this.password,
    required this.publicKey,
  });

  Map<String, dynamic> toJson() => {
    'Username': username,
    'Email': email,
    'Password': password,
    'PublicKey': publicKey,
  };

  factory RegistrationRequest.fromJson(Map<String, dynamic> json) => RegistrationRequest(
    username: json['Username'] as String,
    email: json['Email'] as String,
    password: json['Password'] as String,
    publicKey: json['PublicKey'] as String,
  );

  List<int> toBytes() => utf8.encode(jsonEncode(toJson()));
}

/// Registration response payload
class RegistrationResponse {
  final bool success;
  final String? message;
  final RegisteredUserInfo? user;

  RegistrationResponse({
    required this.success,
    this.message,
    this.user,
  });

  Map<String, dynamic> toJson() => {
    'Success': success,
    if (message != null) 'Message': message,
    if (user != null) 'User': user!.toJson(),
  };

  factory RegistrationResponse.fromJson(Map<String, dynamic> json) => RegistrationResponse(
    success: json['Success'] as bool,
    message: json['Message'] as String?,
    user: json['User'] != null
        ? RegisteredUserInfo.fromJson(json['User'] as Map<String, dynamic>)
        : null,
  );

  factory RegistrationResponse.fromBytes(List<int> bytes) {
    final json = jsonDecode(utf8.decode(bytes)) as Map<String, dynamic>;
    return RegistrationResponse.fromJson(json);
  }
}

/// Minimal registered user info returned by the server
class RegisteredUserInfo {
  final int id;
  final String username;

  RegisteredUserInfo({
    required this.id,
    required this.username,
  });

  Map<String, dynamic> toJson() => {
    'Id': id,
    'Username': username,
  };

  factory RegisteredUserInfo.fromJson(Map<String, dynamic> json) =>
      RegisteredUserInfo(
        id: json['Id'] as int,
        username: json['Username'] as String,
      );
}

/// User search request payload
class UserSearchRequest {
  final String query;
  final int limit;

  UserSearchRequest({
    required this.query,
    this.limit = 20,
  });

  Map<String, dynamic> toJson() => {
    'Query': query,
    'Limit': limit,
  };

  factory UserSearchRequest.fromJson(Map<String, dynamic> json) => UserSearchRequest(
    query: json['Query'] as String,
    limit: json['Limit'] as int? ?? 20,
  );

  List<int> toBytes() => utf8.encode(jsonEncode(toJson()));
}

/// User search response payload
class UserSearchResponse {
  final bool success;
  final List<UserSearchResult> users;
  final String? message;

  UserSearchResponse({
    required this.success,
    required this.users,
    this.message,
  });

  Map<String, dynamic> toJson() => {
    'Success': success,
    'Users': users.map((u) => u.toJson()).toList(),
    if (message != null) 'Message': message,
  };

  factory UserSearchResponse.fromJson(Map<String, dynamic> json) => UserSearchResponse(
    success: json['Success'] as bool,
    users: (json['Users'] as List<dynamic>)
        .map((u) => UserSearchResult.fromJson(u as Map<String, dynamic>))
        .toList(),
    message: json['Message'] as String?,
  );

  factory UserSearchResponse.fromBytes(List<int> bytes) {
    final json = jsonDecode(utf8.decode(bytes)) as Map<String, dynamic>;
    return UserSearchResponse.fromJson(json);
  }
}

/// User search result item
class UserSearchResult {
  final int id;
  final String username;
  final String? email;

  UserSearchResult({
    required this.id,
    required this.username,
    this.email,
  });

  Map<String, dynamic> toJson() => {
    'Id': id,
    'Username': username,
    if (email != null) 'Email': email,
  };

  factory UserSearchResult.fromJson(Map<String, dynamic> json) => UserSearchResult(
    id: json['Id'] as int,
    username: json['Username'] as String,
    email: json['Email'] as String?,
  );
}

/// User entity
class User {
  final int id;
  final String username;
  final String email;
  final String publicKey;
  final String? identityKeyFingerprint;
  final bool isActive;
  final DateTime createdAt;
  final DateTime updatedAt;
  final DateTime? lastSeenAt;

  User({
    required this.id,
    required this.username,
    required this.email,
    required this.publicKey,
    this.identityKeyFingerprint,
    required this.isActive,
    required this.createdAt,
    required this.updatedAt,
    this.lastSeenAt,
  });

  Map<String, dynamic> toJson() => {
    'Id': id,
    'Username': username,
    'Email': email,
    'PublicKey': publicKey,
    if (identityKeyFingerprint != null) 'IdentityKeyFingerprint': identityKeyFingerprint,
    'IsActive': isActive,
    'CreatedAt': createdAt.toIso8601String(),
    'UpdatedAt': updatedAt.toIso8601String(),
    if (lastSeenAt != null) 'LastSeenAt': lastSeenAt!.toIso8601String(),
  };

  factory User.fromJson(Map<String, dynamic> json) => User(
    id: json['Id'] as int,
    username: json['Username'] as String,
    email: json['Email'] as String,
    publicKey: json['PublicKey'] as String,
    identityKeyFingerprint: json['IdentityKeyFingerprint'] as String?,
    isActive: json['IsActive'] as bool,
    createdAt: DateTime.parse(json['CreatedAt'] as String),
    updatedAt: DateTime.parse(json['UpdatedAt'] as String),
    lastSeenAt: json['LastSeenAt'] != null ? DateTime.parse(json['LastSeenAt'] as String) : null,
  );
}

/// Channel message request payload
class ChannelMessageRequest {
  final int channelId;
  final String content;
  final MessageContentType contentType;
  final int? replyToMessageId;

  ChannelMessageRequest({
    required this.channelId,
    required this.content,
    this.contentType = MessageContentType.text,
    this.replyToMessageId,
  });

  Map<String, dynamic> toJson() => {
    'ChannelId': channelId,
    'Content': content,
    'ContentType': contentType.value,
    if (replyToMessageId != null) 'ReplyToMessageId': replyToMessageId,
  };

  factory ChannelMessageRequest.fromJson(Map<String, dynamic> json) => ChannelMessageRequest(
    channelId: json['ChannelId'] as int,
    content: json['Content'] as String,
    contentType: MessageContentType.fromValue(json['ContentType'] as int? ?? 0),
    replyToMessageId: json['ReplyToMessageId'] as int?,
  );

  List<int> toBytes() => utf8.encode(jsonEncode(toJson()));
}

/// Channel message response payload
class ChannelMessageResponse {
  final bool success;
  final int messageId;
  final String? messageText;

  ChannelMessageResponse({
    required this.success,
    this.messageId = 0,
    this.messageText,
  });

  Map<String, dynamic> toJson() => {
    'Success': success,
    'MessageId': messageId,
    if (messageText != null) 'MessageText': messageText,
  };

  factory ChannelMessageResponse.fromJson(Map<String, dynamic> json) => ChannelMessageResponse(
    success: json['Success'] as bool,
    messageId: json['MessageId'] as int? ?? 0,
    messageText: json['MessageText'] as String?,
  );

  factory ChannelMessageResponse.fromBytes(List<int> bytes) {
    final json = jsonDecode(utf8.decode(bytes)) as Map<String, dynamic>;
    return ChannelMessageResponse.fromJson(json);
  }
}

/// Channel message entity
class ChannelMessage {
  final int id;
  final int channelId;
  final int fromUserId;
  final String content;
  final MessageContentType contentType;
  final DateTime createdAt;
  final DateTime? editedAt;
  final bool isEdited;
  final int? replyToMessageId;
  final bool isPinned;

  ChannelMessage({
    required this.id,
    required this.channelId,
    required this.fromUserId,
    required this.content,
    required this.contentType,
    required this.createdAt,
    this.editedAt,
    this.isEdited = false,
    this.replyToMessageId,
    this.isPinned = false,
  });

  Map<String, dynamic> toJson() => {
    'Id': id,
    'ChannelId': channelId,
    'FromUserId': fromUserId,
    'Content': content,
    'ContentType': contentType.value,
    'CreatedAt': createdAt.toIso8601String(),
    if (editedAt != null) 'EditedAt': editedAt!.toIso8601String(),
    'IsEdited': isEdited,
    if (replyToMessageId != null) 'ReplyToMessageId': replyToMessageId,
    'IsPinned': isPinned,
  };

  factory ChannelMessage.fromJson(Map<String, dynamic> json) => ChannelMessage(
    id: json['Id'] as int,
    channelId: json['ChannelId'] as int,
    fromUserId: json['FromUserId'] as int,
    content: json['Content'] as String,
    contentType: MessageContentType.fromValue(json['ContentType'] as int),
    createdAt: DateTime.parse(json['CreatedAt'] as String),
    editedAt: json['EditedAt'] != null ? DateTime.parse(json['EditedAt'] as String) : null,
    isEdited: json['IsEdited'] as bool? ?? false,
    replyToMessageId: json['ReplyToMessageId'] as int?,
    isPinned: json['IsPinned'] as bool? ?? false,
  );
}

/// Channel create request payload
class ChannelCreateRequest {
  final String name;
  final String? description;
  final ChannelType type;

  ChannelCreateRequest({
    required this.name,
    this.description,
    this.type = ChannelType.public,
  });

  Map<String, dynamic> toJson() => {
    'Name': name,
    if (description != null) 'Description': description,
    'Type': type.value,
  };

  factory ChannelCreateRequest.fromJson(Map<String, dynamic> json) => ChannelCreateRequest(
    name: json['Name'] as String,
    description: json['Description'] as String?,
    type: ChannelType.fromValue(json['Type'] as int? ?? 0),
  );

  List<int> toBytes() => utf8.encode(jsonEncode(toJson()));
}

/// Channel create response payload
class ChannelCreateResponse {
  final bool success;
  final int channelId;
  final String? message;

  ChannelCreateResponse({
    required this.success,
    this.channelId = 0,
    this.message,
  });

  Map<String, dynamic> toJson() => {
    'Success': success,
    'ChannelId': channelId,
    if (message != null) 'Message': message,
  };

  factory ChannelCreateResponse.fromJson(Map<String, dynamic> json) => ChannelCreateResponse(
    success: json['Success'] as bool,
    channelId: json['ChannelId'] as int? ?? 0,
    message: json['Message'] as String?,
  );

  factory ChannelCreateResponse.fromBytes(List<int> bytes) {
    final json = jsonDecode(utf8.decode(bytes)) as Map<String, dynamic>;
    return ChannelCreateResponse.fromJson(json);
  }
}

/// Channel entity
class Channel {
  final int id;
  final String name;
  final String? description;
  final ChannelType type;
  final int createdByUserId;
  final DateTime createdAt;
  final DateTime updatedAt;
  final bool isActive;
  final String? inviteCode;
  final int memberCount;

  Channel({
    required this.id,
    required this.name,
    this.description,
    required this.type,
    required this.createdByUserId,
    required this.createdAt,
    required this.updatedAt,
    required this.isActive,
    this.inviteCode,
    required this.memberCount,
  });

  Map<String, dynamic> toJson() => {
    'Id': id,
    'Name': name,
    if (description != null) 'Description': description,
    'Type': type.value,
    'CreatedByUserId': createdByUserId,
    'CreatedAt': createdAt.toIso8601String(),
    'UpdatedAt': updatedAt.toIso8601String(),
    'IsActive': isActive,
    if (inviteCode != null) 'InviteCode': inviteCode,
    'MemberCount': memberCount,
  };

  factory Channel.fromJson(Map<String, dynamic> json) => Channel(
    id: json['Id'] as int,
    name: json['Name'] as String,
    description: json['Description'] as String?,
    type: ChannelType.fromValue(json['Type'] as int),
    createdByUserId: json['CreatedByUserId'] as int,
    createdAt: DateTime.parse(json['CreatedAt'] as String),
    updatedAt: DateTime.parse(json['UpdatedAt'] as String),
    isActive: json['IsActive'] as bool,
    inviteCode: json['InviteCode'] as String?,
    memberCount: json['MemberCount'] as int,
  );
}

/// Minimal channel info returned in join/create responses
class ChannelSummary {
  final int id;
  final String name;
  final String? description;
  final ChannelType type;
  final int memberCount;

  ChannelSummary({
    required this.id,
    required this.name,
    this.description,
    required this.type,
    required this.memberCount,
  });

  Map<String, dynamic> toJson() => {
    'Id': id,
    'Name': name,
    if (description != null) 'Description': description,
    'Type': type.value,
    'MemberCount': memberCount,
  };

  factory ChannelSummary.fromJson(Map<String, dynamic> json) => ChannelSummary(
    id: json['Id'] as int,
    name: json['Name'] as String,
    description: json['Description'] as String?,
    type: ChannelType.fromValue(json['Type'] as int? ?? 0),
    memberCount: json['MemberCount'] as int? ?? 0,
  );
}

/// Channel join request payload
class ChannelJoinRequest {
  final int channelId;

  ChannelJoinRequest({
    required this.channelId,
  });

  Map<String, dynamic> toJson() => {
    'ChannelId': channelId,
  };

  factory ChannelJoinRequest.fromJson(Map<String, dynamic> json) => ChannelJoinRequest(
    channelId: json['ChannelId'] as int,
  );

  List<int> toBytes() => utf8.encode(jsonEncode(toJson()));
}

/// Channel join response payload
class ChannelJoinResponse {
  final bool success;
  final ChannelSummary? channel;
  final String? message;

  ChannelJoinResponse({
    required this.success,
    this.channel,
    this.message,
  });

  Map<String, dynamic> toJson() => {
    'Success': success,
    if (channel != null) 'Channel': channel!.toJson(),
    if (message != null) 'Message': message,
  };

  factory ChannelJoinResponse.fromJson(Map<String, dynamic> json) => ChannelJoinResponse(
    success: json['Success'] as bool,
    channel: json['Channel'] != null
        ? ChannelSummary.fromJson(json['Channel'] as Map<String, dynamic>)
        : null,
    message: json['Message'] as String?,
  );

  factory ChannelJoinResponse.fromBytes(List<int> bytes) {
    final json = jsonDecode(utf8.decode(bytes)) as Map<String, dynamic>;
    return ChannelJoinResponse.fromJson(json);
  }
}

/// Private chat message request payload
class PrivateChatMessageRequest {
  final int toUserId;
  final String content;
  final MessageContentType contentType;

  PrivateChatMessageRequest({
    required this.toUserId,
    required this.content,
    this.contentType = MessageContentType.text,
  });

  Map<String, dynamic> toJson() => {
    'ToUserId': toUserId,
    'Content': content,
    'ContentType': contentType.value,
  };

  factory PrivateChatMessageRequest.fromJson(Map<String, dynamic> json) => PrivateChatMessageRequest(
    toUserId: json['ToUserId'] as int,
    content: json['Content'] as String,
    contentType: MessageContentType.fromValue(json['ContentType'] as int? ?? 0),
  );

  List<int> toBytes() => utf8.encode(jsonEncode(toJson()));
}

/// Private chat message response payload
class PrivateChatMessageResponse {
  final bool success;
  final int messageId;
  final String? messageText;

  PrivateChatMessageResponse({
    required this.success,
    this.messageId = 0,
    this.messageText,
  });

  Map<String, dynamic> toJson() => {
    'Success': success,
    'MessageId': messageId,
    if (messageText != null) 'MessageText': messageText,
  };

  factory PrivateChatMessageResponse.fromJson(Map<String, dynamic> json) => PrivateChatMessageResponse(
    success: json['Success'] as bool,
    messageId: json['MessageId'] as int? ?? 0,
    messageText: json['MessageText'] as String?,
  );

  factory PrivateChatMessageResponse.fromBytes(List<int> bytes) {
    final json = jsonDecode(utf8.decode(bytes)) as Map<String, dynamic>;
    return PrivateChatMessageResponse.fromJson(json);
  }
}

/// Message entity (stored/delivered message, not the wire-level frame)
class ChatMessage {
  final int id;
  final int fromUserId;
  final int toUserId;
  final String content;
  final MessageContentType contentType;
  final int sequenceNumber;
  final bool isDelivered;
  final bool isRead;
  final DateTime createdAt;
  final DateTime? deliveredAt;
  final DateTime? readAt;

  ChatMessage({
    required this.id,
    required this.fromUserId,
    required this.toUserId,
    required this.content,
    required this.contentType,
    required this.sequenceNumber,
    this.isDelivered = false,
    this.isRead = false,
    required this.createdAt,
    this.deliveredAt,
    this.readAt,
  });

  Map<String, dynamic> toJson() => {
    'Id': id,
    'FromUserId': fromUserId,
    'ToUserId': toUserId,
    'Content': content,
    'ContentType': contentType.value,
    'SequenceNumber': sequenceNumber,
    'IsDelivered': isDelivered,
    'IsRead': isRead,
    'CreatedAt': createdAt.toIso8601String(),
    if (deliveredAt != null) 'DeliveredAt': deliveredAt!.toIso8601String(),
    if (readAt != null) 'ReadAt': readAt!.toIso8601String(),
  };

  factory ChatMessage.fromJson(Map<String, dynamic> json) => ChatMessage(
    id: json['Id'] as int,
    fromUserId: json['FromUserId'] as int,
    toUserId: json['ToUserId'] as int,
    content: json['Content'] as String,
    contentType: MessageContentType.fromValue(json['ContentType'] as int),
    sequenceNumber: json['SequenceNumber'] as int,
    isDelivered: json['IsDelivered'] as bool? ?? false,
    isRead: json['IsRead'] as bool? ?? false,
    createdAt: DateTime.parse(json['CreatedAt'] as String),
    deliveredAt: json['DeliveredAt'] != null ? DateTime.parse(json['DeliveredAt'] as String) : null,
    readAt: json['ReadAt'] != null ? DateTime.parse(json['ReadAt'] as String) : null,
  );
}

/// Private chat entity
class PrivateChat {
  final int id;
  final int user1Id;
  final int user2Id;
  final DateTime createdAt;
  final DateTime? lastActivityAt;
  final int? lastMessageId;
  final bool isActive;
  final ChatMessage? lastMessage;

  PrivateChat({
    required this.id,
    required this.user1Id,
    required this.user2Id,
    required this.createdAt,
    this.lastActivityAt,
    this.lastMessageId,
    this.isActive = true,
    this.lastMessage,
  });

  Map<String, dynamic> toJson() => {
    'Id': id,
    'User1Id': user1Id,
    'User2Id': user2Id,
    'CreatedAt': createdAt.toIso8601String(),
    if (lastActivityAt != null) 'LastActivityAt': lastActivityAt!.toIso8601String(),
    if (lastMessageId != null) 'LastMessageId': lastMessageId,
    'IsActive': isActive,
    if (lastMessage != null) 'LastMessage': lastMessage!.toJson(),
  };

  factory PrivateChat.fromJson(Map<String, dynamic> json) => PrivateChat(
    id: json['Id'] as int,
    user1Id: json['User1Id'] as int,
    user2Id: json['User2Id'] as int,
    createdAt: DateTime.parse(json['CreatedAt'] as String),
    lastActivityAt: json['LastActivityAt'] != null ? DateTime.parse(json['LastActivityAt'] as String) : null,
    lastMessageId: json['LastMessageId'] as int?,
    isActive: json['IsActive'] as bool? ?? true,
    lastMessage: json['LastMessage'] != null ? ChatMessage.fromJson(json['LastMessage'] as Map<String, dynamic>) : null,
  );
}

// ─── Profile payloads ────────────────────────────────────────────────────────

/// Profile data returned by the server
class ProfileData {
  final int id;
  final String username;
  final String? displayName;
  final String? avatarUrl;
  final String? bio;
  final String? email;
  final DateTime createdAt;
  final DateTime? lastSeenAt;

  ProfileData({
    required this.id,
    required this.username,
    this.displayName,
    this.avatarUrl,
    this.bio,
    this.email,
    required this.createdAt,
    this.lastSeenAt,
  });

  Map<String, dynamic> toJson() => {
    'Id': id,
    'Username': username,
    if (displayName != null) 'DisplayName': displayName,
    if (avatarUrl != null) 'AvatarUrl': avatarUrl,
    if (bio != null) 'Bio': bio,
    if (email != null) 'Email': email,
    'CreatedAt': createdAt.toIso8601String(),
    if (lastSeenAt != null) 'LastSeenAt': lastSeenAt!.toIso8601String(),
  };

  factory ProfileData.fromJson(Map<String, dynamic> json) => ProfileData(
    id: json['Id'] as int,
    username: json['Username'] as String,
    displayName: json['DisplayName'] as String?,
    avatarUrl: json['AvatarUrl'] as String?,
    bio: json['Bio'] as String?,
    email: json['Email'] as String?,
    createdAt: DateTime.parse(json['CreatedAt'] as String),
    lastSeenAt: json['LastSeenAt'] != null
        ? DateTime.parse(json['LastSeenAt'] as String)
        : null,
  );
}

/// Request to update the authenticated user's profile
class ProfileUpdateRequest {
  final String? displayName;
  final String? avatarUrl;
  final String? bio;
  final String? username;

  ProfileUpdateRequest({
    this.displayName,
    this.avatarUrl,
    this.bio,
    this.username,
  });

  Map<String, dynamic> toJson() => {
    if (displayName != null) 'DisplayName': displayName,
    if (avatarUrl != null) 'AvatarUrl': avatarUrl,
    if (bio != null) 'Bio': bio,
    if (username != null) 'Username': username,
  };

  List<int> toBytes() => utf8.encode(jsonEncode(toJson()));
}

/// Response to a profile update
class ProfileUpdateResponse {
  final bool success;
  final String? message;
  final ProfileData? profile;

  ProfileUpdateResponse({
    required this.success,
    this.message,
    this.profile,
  });

  factory ProfileUpdateResponse.fromJson(Map<String, dynamic> json) =>
      ProfileUpdateResponse(
        success: json['Success'] as bool,
        message: json['Message'] as String?,
        profile: json['Profile'] != null
            ? ProfileData.fromJson(json['Profile'] as Map<String, dynamic>)
            : null,
      );

  factory ProfileUpdateResponse.fromBytes(List<int> bytes) {
    final json = jsonDecode(utf8.decode(bytes)) as Map<String, dynamic>;
    return ProfileUpdateResponse.fromJson(json);
  }
}

/// Request to get a user's profile
class ProfileGetRequest {
  final int? userId;
  final String? username;

  ProfileGetRequest({this.userId, this.username});

  Map<String, dynamic> toJson() => {
    if (userId != null) 'UserId': userId,
    if (username != null) 'Username': username,
  };

  List<int> toBytes() => utf8.encode(jsonEncode(toJson()));
}

/// Response to a profile get request
class ProfileGetResponse {
  final bool success;
  final ProfileData? profile;
  final String? message;

  ProfileGetResponse({
    required this.success,
    this.profile,
    this.message,
  });

  factory ProfileGetResponse.fromJson(Map<String, dynamic> json) =>
      ProfileGetResponse(
        success: json['Success'] as bool,
        profile: json['Profile'] != null
            ? ProfileData.fromJson(json['Profile'] as Map<String, dynamic>)
            : null,
        message: json['Message'] as String?,
      );

  factory ProfileGetResponse.fromBytes(List<int> bytes) {
    final json = jsonDecode(utf8.decode(bytes)) as Map<String, dynamic>;
    return ProfileGetResponse.fromJson(json);
  }
}

// ─── Channel edit payloads ────────────────────────────────────────────────────

/// Request to edit a channel (name, description, avatar)
class ChannelEditRequest {
  final int channelId;
  final String? name;
  final String? description;
  final String? avatarUrl;

  ChannelEditRequest({
    required this.channelId,
    this.name,
    this.description,
    this.avatarUrl,
  });

  Map<String, dynamic> toJson() => {
    'ChannelId': channelId,
    if (name != null) 'Name': name,
    if (description != null) 'Description': description,
    if (avatarUrl != null) 'AvatarUrl': avatarUrl,
  };

  List<int> toBytes() => utf8.encode(jsonEncode(toJson()));
}

/// Response to a channel edit request
class ChannelEditResponse {
  final bool success;
  final String? message;

  ChannelEditResponse({required this.success, this.message});

  factory ChannelEditResponse.fromJson(Map<String, dynamic> json) =>
      ChannelEditResponse(
        success: json['Success'] as bool,
        message: json['Message'] as String?,
      );

  factory ChannelEditResponse.fromBytes(List<int> bytes) {
    final json = jsonDecode(utf8.decode(bytes)) as Map<String, dynamic>;
    return ChannelEditResponse.fromJson(json);
  }
}

// ─── Group edit payloads ──────────────────────────────────────────────────────

/// Request to edit a group chat (name, description, avatar)
class GroupEditRequest {
  final int groupId;
  final String? name;
  final String? description;
  final String? avatarUrl;

  GroupEditRequest({
    required this.groupId,
    this.name,
    this.description,
    this.avatarUrl,
  });

  Map<String, dynamic> toJson() => {
    'GroupId': groupId,
    if (name != null) 'Name': name,
    if (description != null) 'Description': description,
    if (avatarUrl != null) 'AvatarUrl': avatarUrl,
  };

  List<int> toBytes() => utf8.encode(jsonEncode(toJson()));
}

/// Response to a group edit request
class GroupEditResponse {
  final bool success;
  final String? message;

  GroupEditResponse({required this.success, this.message});

  factory GroupEditResponse.fromJson(Map<String, dynamic> json) =>
      GroupEditResponse(
        success: json['Success'] as bool,
        message: json['Message'] as String?,
      );

  factory GroupEditResponse.fromBytes(List<int> bytes) {
    final json = jsonDecode(utf8.decode(bytes)) as Map<String, dynamic>;
    return GroupEditResponse.fromJson(json);
  }
}
