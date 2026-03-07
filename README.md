# Серверный протокол Aegis

Высокопроизводительный TCP-протокол для мессенджера TwoSpace с собственным бинарным шифрованием, поддержкой локальной базы данных, регистрацией пользователей, каналами и приватными чатами.

## Новые возможности

### 🚀 База данных и пользователи
- **Локальная SQLite база данных** с Entity Framework Core
- **Регистрация пользователей** с username, email и паролем
- **Аутентификация** по токенам сессий
- **Поиск пользователей** по username с поддержкой шаблонов

### 💬 Каналы и чаты
- **Каналы** трех типов: публичные, приватные, групповые
- **Управление участниками** с ролями (Member, Moderator, Admin, Owner)
- **Приватные чаты** между двумя пользователями
- **История сообщений** с поддержкой ответов и закрепления

### 🔐 Безопасность
- **End-to-end шифрование** сообщений
- **Хеширование паролей** с использованием современных алгоритмов
- **X3DH протокол** для ключевого обмена
- **MAC проверка** целостности сообщений

## Архитектура

```text
Клиент <-> TCP Сервер <-> Роутер сообщений <-> Обработчики
                     ↑
               Криптографический слой
```

## Структура проекта

```text
AegisMessenger.Server/
├── src/
│   ├── Aegis.Common/          # Общие интерфейсы и типы ошибок
│   │   ├── Errors/            # Классы пользовательских исключений
│   │   ├── Logging/           # Интерфейс логгера
│   │   └── ICryptoProvider.cs # Интерфейсы криптографии
│   ├── Aegis.Protocol/        # Реализация бинарного протокола
│   │   ├── Message.cs         # Структура данных сообщения
│   │   ├── MessageEncoder.cs  # Сериализация/десериализация
│   │   ├── MessageType.cs     # Перечисление типов сообщений
│   │   └── ProtocolConstants.cs # Константы протокола
│   ├── Aegis.Data/           # Слой данных с Entity Framework Core
│   │   ├── Entities/         # Сущности базы данных
│   │   │   └── DataEntities.cs # User, Channel, Message и др.
│   │   ├── Repositories/     # Репозитории для работы с БД
│   │   │   └── Repositories.cs # UserRepository, ChannelRepository и др.
│   │   ├── Services/         # Бизнес-логика
│   │   │   └── UserServices.cs # Регистрация, аутентификация, поиск
│   │   └── AegisDbContext.cs # Контекст Entity Framework
│   ├── Aegis.Crypto/          # Криптографический слой
│   │   ├── ICryptoProvider.cs # Интерфейс криптографии
│   │   └── AegisCryptoProvider.cs # Реализация AES-GCM + HMAC
│   ├── Aegis.Transport/       # Транспортный TCP слой
│   │   ├── ConnectionContext.cs # Состояние клиентского соединения
│   │   └── TcpServer.cs       # Асинхронный TCP сервер
│   ├── Aegis.Handlers/        # Обработчики сообщений
│   │   ├── IMessageHandler.cs # Интерфейс обработчика
│   │   ├── MessageRouter.cs   # Маршрутизация сообщений
│   │   ├── AuthHandler.cs     # Обработчик аутентификации
│   │   ├── PingHandler.cs     # Обработчик keep-alive
│   │   ├── MessageHandler.cs  # Обработчик сообщений чата
│   │   ├── UserHandlers.cs    # Обработчики регистрации и поиска
│   │   ├── ChannelHandlers.cs # Обработчики каналов и чатов
│   │   └── AntiSpamClient.cs  # Интеграция с антиспамом
│   └── Aegis.Server/          # Основное приложение сервера
│       └── Program.cs          # Точка входа и запуск сервера
└── tests/
    └── Aegis.Tests/           # Юнит-тесты
```

## Формат бинарного протокола

```text
0                   1                   2                   3
0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                            Magic                              |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|   ВерсияМаж  |   ВерсияМин  |     Флаги     |   ТипСообщ   ...
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
...             |            ID последовательности              |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|         ID последовательности (продолжение)      | ДлинаПолезн...
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
...             |                  Полезная нагрузка             |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                             MAC                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
```

### Поля протокола

- **Magic (4 байта)**: `0xAE6C5D7` - Идентификатор протокола
- **VersionMajor (1 байт)**: Старшая версия протокола (текущая 1)
- **VersionMinor (1 байт)**: Младшая версия протокола (текущая 0)
- **Flags (1 байт)**: Флаги сообщения (зарезервировано для будущего)
- **MessageType (2 байта)**: Идентификатор типа сообщения
- **SequenceId (8 байт)**: Монотонно увеличивающийся номер последовательности
- **PayloadLength (4 байта)**: Длина полезной нагрузки сообщения
- **Payload (переменная)**: Данные полезной нагрузки
- **MAC (32 байта)**: HMAC-SHA256 для аутентификации сообщения

## Типы сообщений

| Тип | Значение | Описание |
|------|-------|-------------|
| Unknown | 0 | Недопустимый/неизвестный тип сообщения |
| Auth | 1 | Запрос/ответ аутентификации |
| Ping | 2 | Ping для поддержания соединения |
| Message | 3 | Сообщение чата |
| Ack | 4 | Подтверждение получения |
| Error | 5 | Ответ с ошибкой |
| Handshake | 6 | Начальное рукопожатие |
| Nack | 7 | Negative acknowledgment |
| RetransmitRequest | 8 | Запрос повторной отправки |
| UserPresence | 9 | Статус пользователя |
| GroupMessage | 10 | Групповое сообщение |
| GroupCreate | 11 | Создание группы |
| GroupLeave | 12 | Выход из группы |
| **ChannelMessage** | **13** | **Сообщение в канале** |
| **ChannelCreate** | **14** | **Создание канала** |
| **ChannelJoin** | **15** | **Присоединение к каналу** |
| **ChannelLeave** | **16** | **Выход из канала** |
| **PrivateChatMessage** | **17** | **Приватное сообщение** |
| **UserSearch** | **18** | **Поиск пользователей** |
| **UserSearchResult** | **19** | **Результаты поиска** |
| **Register** | **20** | **Регистрация пользователя** |
| **RegisterResponse** | **21** | **Ответ регистрации** |

### Новые типы сообщений

#### Регистрация пользователя
```json
{
  "Type": "Register",
  "Payload": {
    "Username": "john_doe",
    "Email": "john@example.com",
    "Password": "secure_password",
    "PublicKey": "base64_public_key"
  }
}
```

#### Поиск пользователей
```json
{
  "Type": "UserSearch",
  "Payload": {
    "Query": "john",
    "Limit": 20
  }
}
```

#### Сообщение в канал
```json
{
  "Type": "ChannelMessage",
  "Payload": {
    "ChannelId": 1,
    "Content": "Hello, world!",
    "ContentType": 0,
    "ReplyToMessageId": null
  }
}
```

## Основные компоненты

### Aegis.Common

#### Ошибки

- **ProtocolError**: Появляется при нарушениях протокола
- **CryptoError**: Появляется при ошибках криптографии
- **TransportError**: Появляется при проблемах сети/транспорта

#### Логирование

- **ILogger**: Интерфейс для структурированного логирования с уровнями ошибок Debug, Info, Warning, Error

### Aegis.Protocol

#### Message

Представляет протокольное сообщение со всеми полями заголовка и полезной нагрузкой.

#### MessageEncoder

- **Encode(Message, Span<byte>)**: Кодирует сообщение в байтовый буфер
- **Decode(ReadOnlySpan<byte>)**: Декодирует сообщение из байтового буфера
- Использует порядок байтов big-endian для кроссплатформенной совместимости
- Валидирует магическое число и размеры полезной нагрузки

### Aegis.Crypto

#### ICryptoProvider

Интерфейс для криптографических операций:

- **DeriveKeys()**: Вывод ключей HKDF
- **Encrypt()**: Шифрование AES-GCM
- **Decrypt()**: Расшифровка AES-GCM
- **ComputeMac()**: Вычисление HMAC-SHA256
- **VerifyMac()**: Проверка MAC

#### AegisCryptoProvider

Реализация для production использования:

- AES-256-GCM для шифрования
- HMAC-SHA256 для аутентификации
- HKDF для вывода ключей
- Безопасная очистка памяти

### Aegis.Transport

#### ConnectionContext

Управляет состоянием клиентского соединения:

- **Socket**: TCP сокет соединения
- **ConnectionId**: Уникальный идентификатор соединения
- **NextSequenceId**: Генератор номеров последовательности
- **LastActivity**: Временная метка соединения
- **Управление буферами**: Использует ArrayPool для производительности

#### TcpServer

Высокопроизводительный асинхронный TCP сервер:

- **StartAsync()**: Начинает прослушивание соединений
- **SendAsync()**: Отправляет данные конкретному клиенту
- **Stop()**: Корректное завершение работы сервера
- **Пул соединений**: Поддерживает 10,000+ одновременных соединений
- **Ориентирован на события**: OnClientConnected, OnClientDisconnected, OnMessageReceived

### Aegis.Handlers

#### IMessageHandler

Интерфейс для обработки сообщений:

- **Type**: Тип сообщения, который обрабатывает обработчик
- **HandleAsync()**: Метод асинхронной обработки сообщения

#### MessageRouter

Маршрутизирует входящие сообщения соответствующим обработчикам на основе MessageType.

#### AuthHandler

Обрабатывает сообщения аутентификации (в настоящее время заглушка).

#### PingHandler

Обрабатывает ping сообщения для мониторинга состояния соединения.

#### MessageHandler

Обрабатывает сообщения чата с интеграцией антиспама:

- Валидирует сообщения через AntiSpamClient
- Отправляет подтверждения получения
- Маршрутизирует сообщения получателям

#### AntiSpamClient

Точка интеграции для внешнего сервиса антиспама (в настоящее время заглушка).

### Aegis.Server

#### Program

Основная точка входа приложения:

- **Main()**: Запуск и конфигурация сервера
- **ProcessMessageAsync()**: Конвейер обработки сообщений
- **ConsoleLogger**: Реализация логирования по умолчанию
- **Корректное завершение**: При помощи Ctrl+C

## Getting Started

### Требования

- .NET 10.0
- SQLite
- Entity Framework Core

### Быстрый запуск

1. **Клонирование и сборка:**
```bash
git clone <repository-url>
cd Aegis
dotnet build
```

2. **Настройка базы данных:**
```bash
cd src/Aegis.Data
dotnet ef migrations add InitialCreate
dotnet ef database update
```

3. **Запуск сервера:**
```bash
dotnet run --project src/Aegis.Server/Aegis.Server.csproj
```

Сервер запустится на порту 8888 и создаст файл базы данных `aegis.db`.

### Конфигурация

Файл `appsettings.json` уже настроен для локальной разработки:

```json
{
  "Server": {
    "Port": 8888,
    "MaxConnections": 10000
  },
  "Database": {
    "Provider": "Sqlite",
    "ConnectionString": "Data Source=aegis.db"
  },
  "Logging": {
    "MinimumLevel": "Information",
    "Console": true,
    "File": true
  }
}
```

## Extensibility Points

1. **Crypto Provider:** Implement `ICryptoProvider` for custom cryptography
2. **Message Handlers:** Implement `IMessageHandler` for new message types
3. **Anti-Spam:** Extend `AntiSpamClient` for external service integration
4. **Logger:** Implement `ILogger` for custom logging backends

## Performance Features

- Zero-copy message encoding/decoding
- ArrayPool-based buffer management
- Async/await throughout
- Connection pooling ready
- Span<T> optimized

## Security Features

- AES-GCM encryption (confidentiality + integrity)
- HMAC-SHA256 authentication
- Forward secrecy ready (key rotation)
- Secure memory cleanup
- Protocol validation

## Usage Examples

### Creating a Custom Message Handler

```csharp
public class CustomHandler : IMessageHandler
{
    public MessageType Type => MessageType.Custom;
    
    public async ValueTask HandleAsync(ConnectionContext context, Message message)
    {
        // Process custom message
        var payload = Encoding.UTF8.GetString(message.Payload);
        // ... handle logic
    }
}
```

### Sending a Message

```csharp
var message = new Message
{
    Magic = ProtocolConstants.Magic,
    VersionMajor = ProtocolConstants.VersionMajor,
    VersionMinor = ProtocolConstants.VersionMinor,
    Type = MessageType.Message,
    SequenceId = context.GetNextSequenceId(),
    Payload = Encoding.UTF8.GetBytes("Hello World!")
};

var buffer = new byte[MessageEncoder.TotalSize(message)];
MessageEncoder.Encode(message, buffer);
await server.SendAsync(context, buffer);
```

## Детальная архитектура

### Поток обработки сообщений

1. **Прием сообщения:** TCP сервер получает сырые байты
2. **Декодирование:** MessageEncoder преобразует байты в объект Message
3. **Валидация:** Проверка магического числа, версии, MAC
4. **Маршрутизация:** MessageRouter направляет сообщение нужному обработчику
5. **Обработка:** Специализированный обработчик выполняет бизнес-логику
6. **Ответ:** Отправка ACK или сообщения об ошибке

### Управление соединениями

- **ConnectionId:** Уникальный 64-битный идентификатор
- **SequenceId:** Монотонно увеличивается для каждого соединения
- **LastActivity:** Отслеживается для таймаутов неактивных соединений
- **Буферы:** Переиспользуются через ArrayPool для минимизации GC

### Криптографический pipeline

1. **Вывод ключей:** HKDF из мастер-ключа
2. **Шифрование:** AES-GCM для конфиденциальности
3. **Аутентификация:** HMAC-SHA256 для целостности
4. **Nonce:** 12-байтный уникальный для каждого сообщения

### Производительность и масштабирование

- **Асинхронность:** Все операции I/O асинхронны
- **Минимум аллокаций:** Использование Span<T> и ArrayPool
- **Параллелизм:** Каждое соединение обрабатывается независимо
- **Масштабируемость:** Тестировано до 10,000+ одновременных соединений

### Мониторинг и отладка

- **Структурированное логирование:** Уровни Debug, Info, Warning, Error
- **Метрики соединений:** Подсчет активных/неактивных соединений
- **Протоколирование:** Детальная информация о сообщениях
- **Обработка ошибок:** Граничное исключения и восстановление

## Конфигурация

### Переменные окружения

- `AEGIS_PORT`: Порт сервера (по умолчанию: 8888)
- `AEGIS_MAX_CONNECTIONS`: Максимальное количество соединений (по умолчанию: 10000)
- `AEGIS_BUFFER_SIZE`: Размер буфера (по умолчанию: 8192)
- `AEGIS_LOG_LEVEL`: Уровень логирования (Debug, Info, Warning, Error)

### Файл конфигурации (appsettings.json)

```json
{
  "Server": {
    "Port": 8888,
    "MaxConnections": 10000,
    "BufferSize": 8192
  },
  "Logging": {
    "LogLevel": "Info",
    "Console": true,
    "File": "aegis.log"
  },
  "Crypto": {
    "KeyRotationInterval": "01:00:00",
    "MasterKeyPath": "/path/to/master.key"
  },
  "AntiSpam": {
    "Enabled": true,
    "Endpoint": "http://localhost:5000",
    "Timeout": "00:00:01"
  }
}
```

## Разработка и тестирование

### Запуск в режиме разработки

```bash
cd src/Aegis.Server
dotnet run --project Aegis.Server.csproj --configuration Debug
```

### Юнит-тесты

```bash
dotnet test tests/Aegis.Tests --logger "console;verbosity=detailed"
```

### Интеграционные тесты

Проект включает заглушки для:
- Внешнего антиспам сервиса
- Системы аутентификации
- Хранилища сообщений

### Рекомендации по разработке

1. **Используйте async/await** для всех I/O операций
2. **Избегайте блокирующих вызовов** в обработчиках сообщений
3. **Применяйте ArrayPool** для временных буферов
4. **Валидируйте все входные данные** перед обработкой
5. **Логируйте ошибки** с контекстом соединения

## Развертывание

### Docker

```dockerfile
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/Aegis.Server/Aegis.Server.csproj", "src/Aegis.Server/"]
RUN dotnet restore "src/Aegis.Server/Aegis.Server.csproj"
COPY . .
WORKDIR "/src/src/Aegis.Server"
RUN dotnet build "Aegis.Server.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Aegis.Server.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Aegis.Server.dll"]
```

### Kubernetes

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: aegis-server
spec:
  replicas: 3
  selector:
    matchLabels:
      app: aegis-server
  template:
    metadata:
      labels:
        app: aegis-server
    spec:
      containers:
      - name: aegis-server
        image: aegis-server:latest
        ports:
        - containerPort: 8888
        env:
        - name: AEGIS_PORT
          value: "8888"
        - name: AEGIS_MAX_CONNECTIONS
          value: "10000"
```

## Траблшутинг

### Частые проблемы

1. **Превышено количество соединений**
   - Увеличьте `AEGIS_MAX_CONNECTIONS`
   - Проверьте таймауты неактивных соединений

2. **Ошибки декодирования сообщений**
   - Проверьте магическое число `0xAE6C5D7`
   - Убедитесь в правильной длине полезной нагрузки

3. **Криптографические ошибки**
   - Проверьте синхронизацию ключей между клиентом и сервером
   - Убедитесь в правильности nonce для AES-GCM

### Логирование для отладки

```csharp
_logger.Debug($"Processing message {message.Type} from connection {context.ConnectionId}");
_logger.Info($"Message routed to {handler.GetType().Name}");
_logger.Warning($"Connection {context.ConnectionId} inactive for {timeout}");
_logger.Error($"Protocol error: {ex.Message}", ex);
```

## Лицензия и поддержка

Проект распространяется под лицензией MIT. Для поддержки и вопросов:
- GitHub Issues: https://github.com/C0dwiz/Aegis/issues
- Документация: [ссылка на документацию]
- Сообщество: https://t.me/twospace_messenger
