# Aegis Python Client

Python клиентская библиотека для протокола Aegis Messenger - высокопроизводительного TCP протокола с бинарным форматом и шифрованием.

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

Скопируйте директорию `aegis_client` в ваш проект или установите через pip (когда будет опубликовано):

```bash
pip install aegis-client
```

## Быстрый старт

```python
from aegis_client import AegisClient, MessageContentType, ChannelType

def main():
    client = AegisClient()
    
    try:
        # Подключение к серверу
        client.connect('localhost', 8888)

        # Если на сервере включен Server:EnableTransportMasking
        # client.connect('localhost', 8888, transport_masking_key='your-shared-mask-key')
        # По умолчанию включен auto-fallback: при handshake fail в masking-режиме
        # клиент автоматически переподключится без masking.
        
        # Регистрация нового пользователя
        registration_response = client.register(
            'username',
            'email@example.com',
            'password',
            'public_key'
        )
        
        if registration_response.success:
            print(f'User registered: {registration_response.user.username}')
        
        # Аутентификация
        client.authenticate('your_auth_token')
        
        # Поиск пользователей
        search_response = client.search_users('username_pattern')
        print(f'Found {len(search_response.users)} users')
        
        # Создание канала
        channel_response = client.create_channel(
            'My Channel',
            description='A test channel',
            channel_type=ChannelType.PUBLIC
        )
        
        if channel_response.success:
            # Присоединение к каналу
            client.join_channel(channel_response.channel.id)
            
            # Отправка сообщения в канал
            client.send_channel_message(
                channel_response.channel.id,
                'Hello from Python client!'
            )
        
        # Отправка приватного сообщения
        client.send_private_message(
            target_user_id,
            'Hello! This is a private message.'
        )
        
        # Прослушивание входящих сообщений
        def handle_message(message):
            print(f'Received: {message.type}')
        
        client.messages.listen = handle_message
        
    except Exception as e:
        print(f'Error: {e}')
    finally:
        client.disconnect()
        client.dispose()

if __name__ == "__main__":
    main()
```

## Основные компоненты

### AegisClient

Основной класс клиента с методами:

#### Базовые методы:
- `connect(host, port, timeout=None, transport_masking_key=None, enable_masking_auto_fallback=True)` - подключение к серверу
- `authenticate(token)` - аутентификация
- `send_message(text, to_user_id)` - отправка сообщения (legacy)
- `ping()` - отправка ping для поддержания соединения
- `disconnect()` - отключение от сервера

#### Новые методы:
- `register(username, email, password, public_key)` - регистрация пользователя
- `search_users(query, limit)` - поиск пользователей
- `create_channel(name, description, channel_type)` - создание канала
- `join_channel(channel_id)` - присоединение к каналу
- `send_channel_message(channel_id, content, content_type, reply_to_message_id)` - отправка сообщения в канал
- `send_private_message(to_user_id, content, content_type)` - отправка приватного сообщения

### Message Payloads

Новые классы для работы с payloads:

#### Пользователи и регистрация:
```python
# Регистрация
registration = RegistrationRequest(
    username='user',
    email='user@example.com',
    password='password',
    public_key='public_key'
)

# Ответ регистрации
response = RegistrationResponse.from_bytes(message.payload)
```

#### Поиск пользователей:
```python
# Запрос поиска
search_request = UserSearchRequest(query='user', limit=10)

# Ответ поиска
search_response = UserSearchResponse.from_bytes(message.payload)
print(f'Found users: {[u.username for u in search_response.users]}')
```

#### Каналы:
```python
# Создание канала
channel_request = ChannelCreateRequest(
    name='General',
    description='General discussion',
    type=ChannelType.PUBLIC
)

# Сообщение в канал
channel_message = ChannelMessageRequest(
    channel_id=123,
    content='Hello everyone!',
    content_type=MessageContentType.TEXT,
    reply_to_message_id=456
)
```

#### Приватные сообщения:
```python
# Приватное сообщение
private_message = PrivateChatMessageRequest(
    to_user_id=789,
    content='Private conversation',
    content_type=MessageContentType.TEXT
)
```

### Типы сообщений

#### Базовые типы:
- `MessageType.UNKNOWN` - неизвестный тип
- `MessageType.AUTH` - аутентификация
- `MessageType.PING` - keep-alive
- `MessageType.MESSAGE` - сообщение чата
- `MessageType.ACK` - подтверждение
- `MessageType.ERROR` - ошибка
- `MessageType.HANDSHAKE` - рукопожатие

#### Новые типы:
- `MessageType.REGISTER` - регистрация пользователя
- `MessageType.REGISTER_RESPONSE` - ответ регистрации
- `MessageType.USER_SEARCH` - поиск пользователей
- `MessageType.USER_SEARCH_RESULT` - результат поиска
- `MessageType.CHANNEL_MESSAGE` - сообщение в канал
- `MessageType.CHANNEL_CREATE` - создание канала
- `MessageType.CHANNEL_JOIN` - присоединение к каналу
- `MessageType.CHANNEL_LEAVE` - выход из канала
- `MessageType.PRIVATE_CHAT_MESSAGE` - приватное сообщение

### Типы контента сообщений

```python
class MessageContentType:
    TEXT = 0      # Текстовое сообщение
    IMAGE = 1     # Изображение
    VIDEO = 2     # Видео
    AUDIO = 3     # Аудио
    FILE = 4      # Файл
    LOCATION = 5  # Геолокация
```

### Типы каналов

```python
class ChannelType:
    PUBLIC = 0    # Публичный канал
    PRIVATE = 1   # Приватный канал
    GROUP = 2     # Групповой чат
```

## Продвинутое использование

### Обработка входящих сообщений

```python
def handle_message(message):
    message_type = message.type.name.lower()
    
    if message_type == 'channel_message':
        response = ChannelMessageResponse.from_bytes(message.payload)
        if response.success:
            print(f'Channel message: {response.message.content}')
    
    elif message_type == 'private_chat_message':
        response = PrivateChatMessageResponse.from_bytes(message.payload)
        if response.success:
            print(f'Private message: {response.message.content}')
    
    elif message_type == 'user_search_result':
        response = UserSearchResponse.from_bytes(message.payload)
        print(f'Search results: {len(response.users)} users found')
    
    elif message_type == 'register_response':
        response = RegistrationResponse.from_bytes(message.payload)
        if response.success:
            print(f'Registration successful: {response.user.username}')
    
    elif message_type == 'ping':
        if len(message.payload) >= 8:
            timestamp = int.from_bytes(message.payload[:8], byteorder='big')
            latency = int(time.time() * 1000) - timestamp
            print(f'Ping: {latency}ms')
    
    elif message_type == 'error':
        if len(message.payload) >= 4:
            error_code = int.from_bytes(message.payload[:2], byteorder='big')
            error_text = message.payload[4:].decode('utf-8', errors='ignore')
            print(f'Error {error_code}: {error_text}')

client.messages.listen = handle_message
```

### Автоматическое переподключение

```python
class RobustClient:
    def __init__(self):
        self.client = AegisClient()
        self.reconnect_timer = None
        self.running = True
    
    def start(self):
        self._connect()
        
        # Обработка разрывов соединения
        def handle_disconnect():
            if self.running:
                self._schedule_reconnect()
        
        self.client.disconnects.listen = handle_disconnect
    
    def _connect(self):
        try:
            self.client.connect('localhost', 8888)
            self.client.authenticate('token')
        except Exception as e:
            if self.running:
                self._schedule_reconnect()
    
    def _schedule_reconnect(self):
        import threading
        import time
        
        def reconnect():
            time.sleep(5)
            if self.running:
                self._connect()
        
        self.reconnect_timer = threading.Timer(5.0, reconnect)
        self.reconnect_timer.start()
```

### Работа с каналами

```python
# Создание публичного канала
channel_response = client.create_channel(
    'General Discussion',
    description='A place for general conversations',
    channel_type=ChannelType.PUBLIC
)

if channel_response.success:
    channel = channel_response.channel
    
    # Присоединение к каналу
    client.join_channel(channel.id)
    
    # Отправка текстового сообщения
    client.send_channel_message(
        channel.id,
        'Hello everyone! 👋',
        content_type=MessageContentType.TEXT
    )
    
    # Отправка ответа на сообщение
    client.send_channel_message(
        channel.id,
        'I agree with your point!',
        reply_to_message_id=previous_message_id
    )
    
    # Отправка другого контента
    client.send_channel_message(
        channel.id,
        'Check out this image!',
        content_type=MessageContentType.IMAGE
    )
```

### Приватные сообщения

```python
# Поиск пользователя для приватного сообщения
search_response = client.search_users('friend_username')
if search_response.success and search_response.users:
    user = search_response.users[0]
    
    # Отправка приватного сообщения
    private_response = client.send_private_message(
        user.id,
        f'Hi {user.username}! Want to chat?',
        content_type=MessageContentType.TEXT
    )
    
    if private_response.success:
        print(f'Private message sent to {user.username}')
        if private_response.private_chat:
            print(f'Private chat ID: {private_response.private_chat.id}')
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

- `AegisException` - базовое исключение
- `ConnectionException` - ошибки подключения
- `NotConnectedException` - попытка операции без подключения
- `TimeoutException` - таймаут операции
- `ProtocolError` - ошибки протокола
- `AuthenticationException` - ошибки аутентификации
- `RegistrationException` - ошибки регистрации

```python
try:
    client.connect('localhost', 8888)
except ConnectionException as e:
    print(f'Connection failed: {e}')
except TimeoutException as e:
    print(f'Connection timeout: {e}')
```

## Примеры

- **Базовый пример:** `examples/basic_example.py` - демонстрирует основные функции
- **Полный пример:** `examples/complete_example.py` - демонстрирует все возможности

## Тестирование

```bash
# Запуск тестов
python -m pytest tests/

# Запуск с покрытием
python -m pytest tests/ --cov=aegis_client
```

## Совместимость

- Python: >=3.8
- Платформы: Windows, macOS, Linux
- Сервер Aegis: v1.0+

## Лицензия

MIT License

## Поддержка

- GitHub Issues: https://github.com/C0dwiz/Aegis/issues
- Документация: [ссылка на документацию]
