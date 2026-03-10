# Aegis Dart Client

Dart клиентская библиотека для протокола Aegis Messenger - высокопроизводительного TCP протокола с бинарным форматом и шифрованием.

## Особенности

- ✅ Полная реализация бинарного протокола Aegis
- ✅ TCP транспортный слой с автоматическим переподключением
- ✅ Поддержка всех типов сообщений (Auth, Ping, Message, Ack, Error, Handshake)
- ✅ **Новые функции:** Регистрация пользователей, поиск пользователей, каналы, приватные сообщения
- ✅ **Новые типы сообщений:** Register, UserSearch, ChannelMessage, PrivateChatMessage и др.
- ✅ Big-endian сериализация для кроссплатформенной совместимости
- ✅ Встроенное логирование и обработка ошибок
- ✅ Stream-based API для обработки входящих сообщений
- ✅ Поддержка Sequence ID для упорядочивания сообщений
- ✅ JSON-сериализация для сложных payloads

## Установка

Добавьте в `pubspec.yaml`:

```yaml
dependencies:
  aegis_client: ^1.0.0
```

## Быстрый старт

```dart
import 'package:aegis_client/aegis_client.dart';

void main() async {
  final client = AegisClient();
  
  try {
    // Подключение к серверу
    await client.connect('localhost', 8888);
    
    // Регистрация нового пользователя
    final registrationResponse = await client.register(
      'username',
      'email@example.com',
      'password',
      'public_key',
    );
    
    if (registrationResponse.success) {
      print('User registered: ${registrationResponse.user?.username}');
    }
    
    // Аутентификация
    await client.authenticate('your_auth_token');
    
    // Поиск пользователей
    final searchResponse = await client.searchUsers('username_pattern');
    print('Found ${searchResponse.users.length} users');
    
    // Создание канала
    final channelResponse = await client.createChannel(
      'My Channel',
      description: 'A test channel',
      type: ChannelType.public,
    );
    
    if (channelResponse.success) {
      // Присоединение к каналу
      await client.joinChannel(channelResponse.channel!.id);
      
      // Отправка сообщения в канал
      await client.sendChannelMessage(
        channelResponse.channel!.id,
        'Hello from Dart client!',
      );
    }
    
    // Отправка приватного сообщения
    await client.sendPrivateMessage(
      targetUserId,
      'Hello! This is a private message.',
    );
    
    // Прослушивание входящих сообщений
    client.messages.listen((message) {
      print('Received: ${message.type}');
    });
    
  } catch (e) {
    print('Error: $e');
  } finally {
    await client.disconnect();
    client.dispose();
  }
}
```

## Основные компоненты

### AegisClient

Основной класс клиента с методами:

#### Базовые методы:
- `connect(host, port)` - подключение к серверу
- `authenticate(token)` - аутентификация
- `sendMessage(text, toUserId)` - отправка сообщения (legacy)
- `ping()` - отправка ping для поддержания соединения
- `disconnect()` - отключение от сервера

#### Новые методы:
- `register(username, email, password, publicKey)` - регистрация пользователя
- `searchUsers(query, limit)` - поиск пользователей
- `createChannel(name, description, type)` - создание канала
- `joinChannel(channelId)` - присоединение к каналу
- `sendChannelMessage(channelId, content, contentType, replyToMessageId)` - отправка сообщения в канал
- `sendPrivateMessage(toUserId, content, contentType)` - отправка приватного сообщения
- `sendPrivatePhoto(toUserId, photoBytes, ...)` - отправка фото в приватный чат
- `sendPrivateFile(toUserId, fileBytes, fileName, ...)` - отправка файла в приватный чат
- `sendPrivateVoice(toUserId, voiceBytes, ...)` - отправка голосового в приватный чат
- `sendChannelPhoto(channelId, photoBytes, ...)` - отправка фото в канал
- `sendChannelFile(channelId, fileBytes, fileName, ...)` - отправка файла в канал
- `sendChannelVoice(channelId, voiceBytes, ...)` - отправка голосового в канал
- `sendMedia(chatType, chatId, mediaBytes, mediaKind, ...)` - единый метод отправки фото/файлов/голосовых в любой чат
- `tryParseMediaAttachment(content, contentType)` - парсинг медиа-вложения из входящего сообщения

### Message Payloads

Новые классы для работы с payloads:

#### Пользователи и регистрация:
```dart
// Регистрация
final registration = RegistrationRequest(
  username: 'user',
  email: 'user@example.com',
  password: 'password',
  publicKey: 'public_key',
);

// Ответ регистрации
final response = RegistrationResponse.fromBytes(message.payload);
```

#### Поиск пользователей:
```dart
// Запрос поиска
final searchRequest = UserSearchRequest(query: 'user', limit: 10);

// Ответ поиска
final searchResponse = UserSearchResponse.fromBytes(message.payload);
print('Found users: ${searchResponse.users.map((u) => u.username)}');
```

#### Каналы:
```dart
// Создание канала
final channelRequest = ChannelCreateRequest(
  name: 'General',
  description: 'General discussion',
  type: ChannelType.public,
);

// Сообщение в канал
final channelMessage = ChannelMessageRequest(
  channelId: 123,
  content: 'Hello everyone!',
  contentType: MessageContentType.text,
  replyToMessageId: 456,
);
```

#### Приватные сообщения:
```dart
// Приватное сообщение
final privateMessage = PrivateChatMessageRequest(
  toUserId: 789,
  content: 'Private conversation',
  contentType: MessageContentType.text,
);

// отправка медиа 
await client.sendMedia(
  chatType: ChatTargetType.private, // private | channel | group
  chatId: 789,
  mediaBytes: fileBytes,
  mediaKind: MediaKind.file,        // photo | file | voice
  fileName: 'report.pdf',
  mimeType: 'application/pdf',
  caption: 'Monthly report',
);

// отправка голосового
await client.sendMedia(
  chatType: ChatTargetType.private,
  chatId: 789,
  mediaBytes: voiceBytes,
  mediaKind: MediaKind.voice,
  fileName: 'voice-note.ogg',
  mimeType: 'audio/ogg',
  caption: 'voice check',
);
```

### Типы сообщений

#### Базовые типы:
- `MessageType.unknown` - неизвестный тип
- `MessageType.auth` - аутентификация
- `MessageType.ping` - keep-alive
- `MessageType.message` - сообщение чата
- `MessageType.ack` - подтверждение
- `MessageType.error` - ошибка
- `MessageType.handshake` - рукопожатие

#### Новые типы:
- `MessageType.register` - регистрация пользователя
- `MessageType.registerResponse` - ответ регистрации
- `MessageType.userSearch` - поиск пользователей
- `MessageType.userSearchResult` - результат поиска
- `MessageType.channelMessage` - сообщение в канал
- `MessageType.channelCreate` - создание канала
- `MessageType.channelJoin` - присоединение к каналу
- `MessageType.channelLeave` - выход из канала
- `MessageType.privateChatMessage` - приватное сообщение

### Типы контента сообщений

```dart
enum MessageContentType {
  text,      // Текстовое сообщение
  image,     // Изображение
  video,     // Видео
  audio,     // Аудио
  file,      // Файл
  location,  // Геолокация
}
```

### Типы каналов

```dart
enum ChannelType {
  public,    // Публичный канал
  private,   // Приватный канал
  group,     // Групповой чат
}
```

## Продвинутое использование

### Обработка входящих сообщений

```dart
client.messages.listen((message) {
  switch (message.type) {
    case MessageType.channelMessage:
      final response = ChannelMessageResponse.fromBytes(message.payload);
      if (response.success) {
        print('Channel message: ${response.message?.content}');
      }
      break;
      
    case MessageType.privateChatMessage:
      final response = PrivateChatMessageResponse.fromBytes(message.payload);
      if (response.success) {
        print('Private message: ${response.message?.content}');
      }
      break;
      
    case MessageType.userSearchResult:
      final response = UserSearchResponse.fromBytes(message.payload);
      print('Search results: ${response.users.length} users found');
      break;
      
    case MessageType.registerResponse:
      final response = RegistrationResponse.fromBytes(message.payload);
      if (response.success) {
        print('Registration successful: ${response.user?.username}');
      }
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
  }
});

client.events.onPrivateMessageEvent((event) {
  if (event.contentType == MessageContentType.audio && event.attachment != null) {
    final voice = event.attachment!;
    final bytes = voice.decodeBytes();
    print('Voice message: file=${voice.fileName}, mime=${voice.mimeType}, bytes=${bytes.length}');
  }
});
```

### Автоматическое переподключение

```dart
class RobustClient {
  late AegisClient _client;
  Timer? _reconnectTimer;
  
  Future<void> start() async {
    await _connect();
    
    // Обработка разрывов соединения
    _client.disconnects.listen((_) {
      _scheduleReconnect();
    });
  }
  
  Future<void> _connect() async {
    try {
      _client = AegisClient();
      await _client.connect('localhost', 8888);
      await _client.authenticate('token');
    } catch (e) {
      _scheduleReconnect();
    }
  }
  
  void _scheduleReconnect() {
    _reconnectTimer = Timer(Duration(seconds: 5), _connect);
  }
}
```

### Работа с каналами

```dart
// Создание публичного канала
final channelResponse = await client.createChannel(
  'General Discussion',
  description: 'A place for general conversations',
  type: ChannelType.public,
);

if (channelResponse.success) {
  final channel = channelResponse.channel!;
  
  // Присоединение к каналу
  await client.joinChannel(channel.id);
  
  // Отправка текстового сообщения
  await client.sendChannelMessage(
    channel.id,
    'Hello everyone! 👋',
    contentType: MessageContentType.text,
  );
  
  // Отправка ответа на сообщение
  await client.sendChannelMessage(
    channel.id,
    'I agree with your point!',
    replyToMessageId: previousMessageId,
  );
  
  // Отправка другого контента
  await client.sendChannelMessage(
    channel.id,
    'Check out this image!',
    contentType: MessageContentType.image,
  );
}
```

### Приватные сообщения

```dart
// Поиск пользователя для приватного сообщения
final searchResponse = await client.searchUsers('friend_username');
if (searchResponse.success && searchResponse.users.isNotEmpty) {
  final user = searchResponse.users.first;
  
  // Отправка приватного сообщения
  final privateResponse = await client.sendPrivateMessage(
    user.id,
    'Hi ${user.username}! Want to chat?',
    contentType: MessageContentType.text,
  );
  
  if (privateResponse.success) {
    print('Private message sent to ${user.username}');
    if (privateResponse.privateChat != null) {
      print('Private chat ID: ${privateResponse.privateChat!.id}');
    }
  }
}
```

## Формат протокола

Бинарный формат сообщения:

```
0                   1                   2                   3
0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                            Magic (4)                     |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|VerMaj|VerMin|Flags |         Message Type (2)          |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                        Sequence ID (8)                  |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                     Sequence ID (cont)                  |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                   Payload Length (4)                    |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                                                               |
|                  Payload (variable)                      |
|                                                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                            MAC (32)                       |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
```

## Константы протокола

- `Magic`: 0xAE6C5D7
- `Version`: 1.0
- `Header Size`: 20 байт
- `MAC Size`: 32 байта (HMAC-SHA256)
- `Max Message Size`: 1MB

## Обработка ошибок

Библиотека предоставляет специальные исключения:

- `ConnectionException` - ошибки подключения
- `NotConnectedException` - попытка операции без подключения
- `TimeoutException` - таймаут операции
- `ProtocolError` - ошибки протокола

```dart
try {
  await client.connect('localhost', 8888);
} on ConnectionException catch (e) {
  print('Connection failed: $e');
} on TimeoutException catch (e) {
  print('Connection timeout: $e');
}
```

## Примеры

- **Базовый пример:** `example/basic_example.dart` - демонстрирует основные функции
- **Полный пример:** `example/complete_example.dart` - демонстрирует все возможности

## Тестирование

```bash
# Запуск тестов
dart test

# Запуск с покрытием
dart test --coverage
```

## Совместимость

- Dart SDK: >=3.0.0
- Платформы: Windows, macOS, Linux, Web
- Сервер Aegis: v1.0+

## Лицензия

MIT License

## Поддержка

- GitHub Issues: https://github.com/C0dwiz/Aegis/issues
- Документация: [ссылка на документацию]
