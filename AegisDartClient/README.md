# Aegis Dart Client

Dart клиентская библиотека для протокола Aegis Messenger - высокопроизводительного TCP протокола с бинарным форматом и шифрованием.

## Особенности

- ✅ Полная реализация бинарного протокола Aegis
- ✅ TCP транспортный слой с автоматическим переподключением
- ✅ Поддержка всех типов сообщений (Auth, Ping, Message, Ack, Error, Handshake)
- ✅ Big-endian сериализация для кроссплатформенной совместимости
- ✅ Встроенное логирование и обработка ошибок
- ✅ Stream-based API для обработки входящих сообщений
- ✅ Поддержка Sequence ID для упорядочивания сообщений

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
    
    // Аутентификация
    await client.authenticate('your_auth_token');
    
    // Отправка сообщения
    await client.sendMessage('Hello from Dart!');
    
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

- `connect(host, port)` - подключение к серверу
- `authenticate(token)` - аутентификация
- `sendMessage(text, toUserId)` - отправка сообщения
- `ping()` - отправка ping для поддержания соединения
- `disconnect()` - отключение от сервера

### Message

Класс сообщения протокола:

```dart
final message = Message.withType(MessageType.message, utf8.encode('Hello'));
message.sequenceId = 1;
message.flags = ProtocolConstants.flagRequiresAck;
```

### MessageEncoder

Сериализация/десериализация сообщений:

```dart
// Кодирование
final data = MessageEncoder.encode(message);

// Декодирование
final decoded = MessageEncoder.decode(data);
```

### Типы сообщений

- `MessageType.unknown` - неизвестный тип
- `MessageType.auth` - аутентификация
- `MessageType.ping` - keep-alive
- `MessageType.message` - сообщение чата
- `MessageType.ack` - подтверждение
- `MessageType.error` - ошибка
- `MessageType.handshake` - рукопожатие

## Продвинутое использование

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

### Обработка входящих сообщений

```dart
client.messages.listen((message) {
  switch (message.type) {
    case MessageType.message:
      final text = String.fromCharCodes(message.payload.sublist(21));
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
  }
});
```

### Настройка логирования

```dart
// Включить логирование
AegisLogger.enabled = true;
AegisLogger.level = LogLevel.debug;

// Отключить логирование
AegisLogger.enabled = false;
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

Базовый пример: `example/basic_example.dart`
Продвинутый пример с переподключением: `example/advanced_example.dart`

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
