# Aegis Protocol — Полное описание

Aegis Protocol — бинарный, сессионный, криптографически защищённый и событийно-ориентированный протокол мессенджера. Работает поверх TCP (опционально с TLS), использует big-endian порядок байтов, MessagePack для сериализации payload и ECDH + AES-GCM для шифрования сессии.

---

## Архитектура: 3 слоя

```
┌─────────────────────────────────────┐
│  App layer  — типы сообщений (#0–69)│
│  (auth, чат, каналы, профили, ...)  │
├─────────────────────────────────────┤
│  Session layer — handshake, ключи,  │
│  anti-replay, шифрование payload    │
├─────────────────────────────────────┤
│  Frame layer — binary frames, ACK,  │
│  порядок, размер, magic/version     │
└─────────────────────────────────────┘
          TCP  (+ optional TLS 1.2/1.3)
```

---

## Транспорт

Клиент открывает постоянное TCP-соединение. Сервер принимает одно соединение на клиента (последнее побеждает — предыдущая сессия вытесняется).

### TLS

TLS включается на уровне конфигурации сервера (`Tls:Enabled = true`). При включении:
- сервер выполняет `SslStream.AuthenticateAsServer` до того, как соединение попадает в протокольный pipeline;
- принудительно TLS 1.2 и TLS 1.3;
- клиент должен выполнить TLS handshake до отправки первого протокольного кадра;
- без TLS все кадры идут голым текстом поверх TCP (защита на уровне сессионного шифрования сохраняется).

### Transport Masking (XOR)

Независимо от TLS, сервер может включить дополнительный XOR-маскинг транспортного потока (`Server:EnableTransportMasking`). Это не криптографическая защита, а мера против простых network scanners/fingerprinting. Ключ маскинга — общий секрет на уровне конфигурации, применяется потоково с накапливаемым смещением.

---

## Формат кадра

```
 0       4   5   6   7    9                17         21
 ┌───────┬───┬───┬───┬────┬────────────────┬──────────┐
 │ Magic │Vmj│Vmn│Flg│Type│  Sequence ID   │PayloadLen│  Payload...
 │4 bytes│1  │1  │1  │ 2  │    8 bytes     │  4 bytes │
 └───────┴───┴───┴───┴────┴────────────────┴──────────┘
```

| Поле         | Размер  | Описание                                          |
|--------------|---------|---------------------------------------------------|
| Magic        | 4 байта | `0x0AE6C5D7` — идентификатор протокола Aegis       |
| VersionMajor | 1 байт  | Текущая версия: `1`                               |
| VersionMinor | 1 байт  | Текущая версия: `0`                               |
| Flags        | 1 байт  | Битовые флаги (см. ниже)                          |
| Type         | 2 байта | `MessageType` (uint16, big-endian)                |
| SequenceId   | 8 байт  | Монотонный uint64, big-endian; защита от replay   |
| PayloadLength| 4 байта | Размер payload в байтах (uint32, big-endian)      |
| Payload      | N байт  | Данные, сериализованные MessagePack               |

Итого заголовок — **21 байт**. Максимальный размер кадра — **1 МБ**.

### Flags (бит-флаги)

| Флаг           | Значение | Описание                            |
|----------------|----------|-------------------------------------|
| `RequiresAck`  | `0x01`   | Сервер/клиент должен ответить ACK   |
| `IsRetransmit` | `0x02`   | Повторная отправка кадра            |
| `Compressed`   | `0x04`   | Payload сжат Brotli                 |
| `Encrypted`    | `0x08`   | Payload зашифрован AES-GCM          |
| `Priority`     | `0x10`   | Высокий приоритет обработки         |

Brotli-сжатие применяется автоматически, когда сырой payload превышает **512 байт**.

---

## Сериализация (MessagePack)

Все payload сериализуются через **MessagePack** с `ContractlessStandardResolver` — строковые ключи, без атрибутов на моделях. После handshake payload может быть дополнительно зашифрован AES-GCM (см. раздел «Безопасность»).

---

## Жизненный цикл сессии

```
Клиент                              Сервер
  │                                   │
  │── TCP connect ─────────────────►  │
  │                                   │
  │── Handshake (type=6) ──────────►  │   клиент отправляет ClientPublicKey (ECDH)
  │◄─ Handshake response ────────────  │   сервер отвечает ServerPublicKey + опц. подпись
  │   (session keys derived)          │   оба выводят сессионный ключ (HKDF / SHA-256)
  │                                   │
  │── Auth (type=1) ───────────────►  │   username/password или token
  │◄─ Auth response ─────────────────  │   UserId, Username, SessionToken
  │                                   │
  │◄═══════ рабочий режим ═══════════►│   все типы сообщений доступны
  │                                   │
  │── Ping (type=2) ───────────────►  │   keepalive, обновляет LastActivity
  │                                   │
  │── ... сообщения ... ────────────►  │
  │                                   │
  │── disconnect ───────────────────►  │   сервер сохраняет offline-presence
```

### Правило прохождения до auth

До успешного **Handshake**: сервер принимает только `Handshake` (6) и `Register` (20). Всё остальное отбрасывается.

До успешного **Auth**: сервер принимает `Handshake`, `Auth`, `Register`, `Ping`. Все прикладные методы вернут ошибку «Not authenticated».

---

## Безопасность

### ECDH Handshake

1. Клиент генерирует `ephemeral` ECDH ключевую пару и отправляет `PublicKey` (base64) в `Handshake` payload.
2. Сервер генерирует свою ephemeral пару, вычисляет `sharedSecret = ECDH(serverPrivate, clientPublic)`, выводит 32-байтовый сессионный ключ через `HKDF/SHA-256`, сохраняет его в `SessionInfo`.
3. Сервер возвращает свой `ServerPublicKey` (base64). Опционально подписывает handshake транскрипт ECDSA P-256 SHA-256 (если включено `RequireSignedHandshakeResponses`).
4. Клиент вычисляет тот же `sharedSecret` и сессионный ключ.

Формат signed-handshake transcript фиксирован и платформенно-независим: `"AEGIS-HANDSHAKE-V1" + len(serverPub)[int32 little-endian] + serverPub + len(clientPub)[int32 little-endian] + clientPub`.

### AES-GCM шифрование payload

После handshake payload шифруется AES-256-GCM. Тег (16 байт) встроен в конец зашифрованного payload — нет отдельного frame-level HMAC. Флаг `Encrypted = 0x08` сигнализирует наличие шифрования в кадре.

При `RequireEncryptedPayloadAfterHandshake = true` нешифрованные payload после handshake отвергаются.

### Anti-replay

`SequenceId` каждого кадра проходит через скользящее окно размером **1024** позиции. Дубли и кадры «за пределами окна» отбрасываются без ответа.

### Rate Limiting

| Ограничение             | Значение по умолчанию |
|-------------------------|-----------------------|
| Auth попыток / мин / IP | 5                     |
| Сообщений / сек         | 100                   |
| Соединений / IP         | 10                    |

Используется Redis-backed rate limiter (`RedisRateLimiter`) в production; ключи скопированы по IP.

---

## ACK/NACK механизм

### ACK (type = 4)

```
Payload: [ SequenceId: 8 bytes big-endian ] [ Status: 1 byte ]
```

Статусы (`AckStatus`):

| Код | Статус          | Смысл                               |
|-----|-----------------|-------------------------------------|
| 0   | `Ok`            | Сообщение успешно обработано        |
| 1   | `Error`         | Ошибка при обработке                |
| 2   | `Retry`         | Требуется повторная отправка        |
| 3   | `NotImplemented`| Тип сообщения не поддерживается     |

### NACK (type = 7)

Сервер возвращает NACK при нарушении целостности или replay-атаке. Клиент должен пересоздать сессию.

### RetransmitRequest (type = 8)

Клиент запрашивает повтор кадра по `SequenceId`. Работает только для кадров в пределах окна ACK-менеджера.

---

## Ping (type = 2)

Клиент отправляет пустой кадр. Сервер обновляет `LastActivity` соединения, ответ не отправляет. Используется как keepalive. Период idle timeout: 900 секунд (по умолчанию).

---

## Все методы (типы сообщений)

Формат записи: `Запрос → Ответ | событие`. Все payload — MessagePack, строковые ключи.
«—» обозначает push-событие от сервера без прямого запроса.

### Аутентификация и регистрация

#### Register (20) → RegisterResponse (21)

Доступен до handshake. Rate-limited.

**Запрос:**
```json
{
  "Username": "alice",
  "Email": "alice@example.com",
  "Password": "s3cr3t",
  "PublicKey": "<base64-encoded-public-key>"
}
```

**Ответ:**
```json
{
  "Success": true,
  "Message": null,
  "User": { "Id": 1001, "Username": "alice" }
}
```

---

#### Handshake (6) → Handshake (6)

Должен быть первым после установки соединения.

**Запрос:**
```json
{
  "PublicKey": "<base64 ECDH ephemeral public key>",
  "ClientVersion": 1
}
```

**Ответ:**
```json
{
  "Success": true,
  "ServerPublicKey": "<base64 ECDH ephemeral public key>",
  "Message": "Handshake established",
  "Signature": "<base64 ECDSA signature, опционально>",
  "SignatureAlgorithm": "ECDSA_P256_SHA256"
}
```

---

#### Auth (1) → Auth (1)

Требует предварительного handshake. Rate-limited.

**Запрос (username/password):**
```json
{
  "Username": "alice",
  "Password": "s3cr3t",
  "ClientInfo": "my-client/1.0"
}
```

**Запрос (token re-auth):**
```json
{
  "Token": "<session_token>",
  "ClientInfo": "my-client/1.0"
}
```

**Ответ:**
```json
{
  "Success": true,
  "UserId": 1001,
  "Username": "alice",
  "SessionToken": "<token для повторной аутентификации>",
  "Error": ""
}
```

При успехе сервер доставляет все накопленные offline-сообщения.

---

### Личные сообщения

#### PrivateChatMessage (17) → PrivateChatMessage (17)

**Запрос:**
```json
{
  "ToUserId": 1002,
  "Content": "Привет!",
  "ContentType": 0,
  "ReplyToMessageId": null,
  "Attachment": null,
  "Attachments": null,
  "ParseMode": null
}
```

`ContentType`: `0` = Text, `1` = Image, `2` = Video, `3` = Audio, `4` = File, `5` = Location.

**Ответ:**
```json
{
  "Success": true,
  "MessageId": 99001,
  "MessageText": null
}
```

Получателю (если online) сервер пушит `PrivateChatMessageEvent (47)`.

---

#### PrivateChatMessageEvent (47) — сервер → клиент

```json
{
  "Id": 99001,
  "FromUserId": 1001,
  "ToUserId": 1002,
  "Content": "Привет!",
  "ContentType": 0,
  "CreatedAt": "2026-03-30T10:00:00Z",
  "DeliveredTo": [1001],
  "ReadBy": [],
  "FromUsername": "alice",
  "Username": "alice"
}
```

---

### Каналы

#### ChannelCreate (14) → ChannelCreate (14)

**Запрос:**
```json
{
  "Name": "general",
  "Description": "Общий канал",
  "Type": 0
}
```

`Type`: `0` = Public, `1` = Private, `2` = Group.

**Ответ:**
```json
{
  "Success": true,
  "ChannelId": 5001,
  "Message": null
}
```

---

#### ChannelJoin (15) → ChannelJoin (15)

**Запрос:**
```json
{ "ChannelId": 5001 }
```

**Ответ:**
```json
{
  "Success": true,
  "Channel": {
    "Id": 5001, "Name": "general", "Description": "...",
    "Type": 0, "MemberCount": 42
  },
  "Message": null
}
```

---

#### ChannelMessage (13) → ChannelMessage (13)

**Запрос:**
```json
{
  "ChannelId": 5001,
  "Content": "Всем привет",
  "ContentType": 0,
  "ReplyToMessageId": null,
  "Attachment": null,
  "Attachments": null,
  "ParseMode": null
}
```

**Ответ:**
```json
{ "Success": true, "MessageId": 99050, "MessageText": "Message sent" }
```

Всем online-участникам пушится `ChannelMessageEvent (48)`.

---

#### ChannelMessageEvent (48) — сервер → клиент

```json
{
  "Id": 99050,
  "ChannelId": 5001,
  "FromUserId": 1001,
  "Content": "Всем привет",
  "ContentType": 0,
  "CreatedAt": "2026-03-30T10:01:00Z",
  "DeliveredTo": [],
  "ReadBy": [],
  "FromUsername": "alice",
  "ChannelName": "general"
}
```

---

#### ChannelEdit (30) → ChannelEditResponse (31)

**Запрос:**
```json
{
  "ChannelId": 5001,
  "Name": "new-name",
  "Description": "Новое описание",
  "AvatarUrl": "https://..."
}
```

---

#### ChannelLinks

| Тип     | Код | Ответ | Описание                         |
|---------|-----|-------|----------------------------------|
| `ChannelLinkUpdate`    | 57 | 58 | Обновить public alias / invite  |
| `ChannelLinkGet`       | 59 | 60 | Получить ссылки канала          |
| `ChannelResolve`       | 61 | 62 | Найти канал по alias/ссылке     |
| `ChannelJoinByLink`    | 63 | 64 | Вступить по invite-ссылке       |

**ChannelLinkUpdate запрос:**
```json
{
  "ChannelId": 5001,
  "PublicAlias": "general-chat",
  "RegeneratePrivateInvite": false
}
```

**ChannelResolve запрос:**
```json
{ "LinkOrAlias": "general-chat" }
```

**ChannelResolve ответ:**
```json
{
  "Success": true,
  "Channel": { "Id": 5001, "Name": "general", "Type": 0, "MemberCount": 42 }
}
```

---

### Группы

| Тип                  | Код | Ответ | Описание                      |
|----------------------|-----|-------|-------------------------------|
| `GroupCreate`        | 11  | 40    | Создать группу                |
| `GroupEdit`          | 32  | 33    | Редактировать группу          |
| `GroupMessageSend`   | 38  | 39    | Отправить сообщение в группу  |
| `GroupLeave`         | 12  | —     | Покинуть группу               |

**GroupCreate запрос:**
```json
{ "Name": "dev-team", "Description": "Команда разработки" }
```

**GroupCreate ответ:**
```json
{ "Success": true, "GroupId": 6001, "Message": null }
```

**GroupMessageSend запрос:**
```json
{
  "GroupId": 6001,
  "Content": "Задеплоил на прод",
  "ContentType": 0,
  "ReplyToMessageId": null,
  "Attachment": null,
  "ParseMode": null
}
```

**GroupMessage (10)** — legacy тип, GroupMessageSend (38) является актуальным.

---

### Управление участниками (admin)

#### MemberRoleUpdate (34) → MemberRoleUpdateResponse (35)

**Запрос:**
```json
{
  "Scope": "channel",
  "TargetId": 5001,
  "TargetUserId": 1002,
  "NewRole": 2
}
```

`Scope`: `"channel"` или `"group"`.

---

#### MemberPermissionUpdate (36) → MemberPermissionUpdateResponse (37)

**Запрос:**
```json
{
  "Scope": "channel",
  "TargetId": 5001,
  "TargetUserId": 1002,
  "CanSendMessages": true,
  "CanDeleteOthersMessages": false,
  "CanEditInfo": false,
  "CanInviteUsers": true,
  "CanRemoveUsers": false,
  "CanPinMessages": false,
  "CanManageRoles": false
}
```

---

### История и контекст (Chat Bootstrap)

#### ChatListRequest (41) → ChatListResponse (42)

Возвращает список всех диалогов пользователя (личные + каналы + группы), отсортированных по последнему сообщению.

**Запрос:** пустой payload или `{}`.

**Ответ:**
```json
{
  "Success": true,
  "Chats": [
    {
      "ChatId": 99001,
      "Type": "private",
      "Title": "Bob",
      "AvatarUrl": null,
      "PresenceStatus": "online",
      "LastMessage": "Привет!",
      "LastMessageAt": "2026-03-30T10:00:00Z",
      "UnreadCount": 2,
      "PeerUserId": 1002,
      "ChannelId": null
    },
    {
      "ChatId": 5001,
      "Type": "channel",
      "Title": "general",
      "AvatarUrl": null,
      "LastMessage": "Всем привет",
      "LastMessageAt": "2026-03-30T10:01:00Z",
      "UnreadCount": 0,
      "PeerUserId": null,
      "ChannelId": 5001
    }
  ]
}
```

---

#### PrivateChatHistoryRequest (43) → PrivateChatHistoryResponse (44)

**Запрос:**
```json
{
  "PeerUserId": 1002,
  "Limit": 100,
  "BeforeMessageId": null
}
```

**Ответ:**
```json
{
  "Success": true,
  "PeerUserId": 1002,
  "Messages": [
    {
      "Id": 99000,
      "FromUserId": 1001,
      "ToUserId": 1002,
      "Content": "Привет!",
      "ContentType": 0,
      "CreatedAt": "2026-03-30T10:00:00Z",
      "DeliveredTo": [1002],
      "ReadBy": [1002],
      "FromUsername": "alice"
    }
  ]
}
```

---

#### ChannelHistoryRequest (45) → ChannelHistoryResponse (46)

**Запрос:**
```json
{
  "ChannelId": 5001,
  "Limit": 100,
  "BeforeMessageId": null
}
```

---

### Редактирование и удаление сообщений

#### MessageEdit (26) → MessageEditResponse (27)

**Запрос:**
```json
{
  "MessageId": 99001,
  "NewContent": "Исправленный текст",
  "Scope": "private",
  "ChannelId": null,
  "GroupId": null
}
```

`Scope`: `"private"`, `"channel"`, `"group"`.

---

#### MessageDelete (28) → MessageDeleteResponse (29)

**Запрос:**
```json
{
  "MessageId": 99001,
  "Scope": "channel",
  "ChannelId": 5001,
  "GroupId": null
}
```

---

### Профиль

#### ProfileUpdate (22) → ProfileUpdateResponse (23)

**Запрос:**
```json
{
  "DisplayName": "Alice Dev",
  "Bio": "# Hello\nI use **Aegis**",
  "AvatarUrl": null,
  "Username": null,
  "Location": "Moscow",
  "BirthDate": "1990-01-15"
}
```

Поле `Bio` сохраняется как есть (сервер не рендерит markdown — это задача UI).

**Ответ:**
```json
{
  "Success": true,
  "Message": null,
  "Profile": { ... }
}
```

---

#### ProfileGet (24) → ProfileGetResponse (25)

**Запрос:**
```json
{ "UserId": 1002 }
```

или по имени:
```json
{ "Username": "bob" }
```

**Ответ:**
```json
{
  "Success": true,
  "Profile": {
    "Id": 1002,
    "Username": "bob",
    "DisplayName": "Bob",
    "AvatarUrl": null,
    "Avatars": [],
    "PresenceStatus": "online",
    "Bio": "...",
    "Location": "SPb",
    "BirthDate": "1995-05-20",
    "Email": null,
    "CreatedAt": "2026-01-01T00:00:00Z",
    "LastSeenAt": "2026-03-30T09:59:00Z"
  }
}
```

`PresenceStatus` возможные значения: `"online"`, `"recently"`, `"long_ago"`.

---

### Аватары профиля

| Тип                        | Код | Ответ | Описание                       |
|----------------------------|-----|-------|--------------------------------|
| `ProfileAvatarAdd`         | 49  | 50    | Добавить аватар                |
| `ProfileAvatarList`        | 51  | 52    | Список аватаров                |
| `ProfileAvatarDelete`      | 53  | 54    | Удалить аватар                 |
| `ProfileAvatarSetPrimary`  | 55  | 56    | Установить основной аватар     |

**ProfileAvatarAdd запрос:**
```json
{
  "AvatarUrl": "data:image/png;base64,...",
  "MakePrimary": true
}
```

**ProfileAvatarAdd ответ:**
```json
{
  "Success": true,
  "Avatar": { "Id": 201, "AvatarUrl": "https://...", "IsPrimary": true, "CreatedAt": "..." }
}
```

---

### Присутствие (Presence)

#### UserPresence (9)

**Запрос:**
```json
{
  "IsOnline": true,
  "ClientTimestamp": "2026-03-30T10:00:00Z"
}
```

Сервер обновляет `LastSeenAt` в БД и возвращает новый статус через `ProfileGetResponse` в последующих запросах.

---

#### UserSearch (18) → UserSearchResult (19)

**Запрос:**
```json
{ "Query": "bob", "Limit": 20 }
```

**Ответ:**
```json
{
  "Success": true,
  "Users": [
    { "Id": 1002, "Username": "bob", "PresenceStatus": "online" }
  ],
  "Message": null
}
```

---

### Квитанции доставки и прочтения

#### MessageDeliveryReceipt (67) → MessageDeliveryReceiptResponse (68)

Клиент уведомляет сервер, что список сообщений был доставлен на устройство.

**Запрос:**
```json
{ "MessageIds": [99001, 99002] }
```

**Ответ:**
```json
{ "Success": true, "MessageIds": [99001, 99002], "ProcessedAt": "..." }
```

Сервер пушит `MessageStatusEvent (69)` отправителю об обновлении статуса.

---

#### MessageReadReceipt (65) → MessageReadReceiptResponse (66)

Клиент уведомляет сервер, что пользователь прочитал список сообщений.

**Запрос:**
```json
{ "MessageIds": [99001, 99002] }
```

---

#### MessageStatusEvent (69) — сервер → отправителю

Push-событие при изменении статуса доставки или прочтения.

**Событие — доставлено:**
```json
{
  "Success": true,
  "MessageIds": [99001, 99002],
  "DeliveredTo": 1002,
  "ReadBy": null,
  "ProcessedAt": "2026-03-30T10:05:00Z"
}
```

**Событие — прочитано:**
```json
{
  "Success": true,
  "MessageIds": [99001],
  "DeliveredTo": null,
  "ReadBy": 1002,
  "ProcessedAt": "2026-03-30T10:06:00Z"
}
```

UI-интерпретация: `✓` = `DeliveredTo` не null; `✓✓` = `ReadBy` не null.

---

## Таблица всех message types

| Код | Имя                          | Направление       | Категория          |
|-----|------------------------------|-------------------|--------------------|
| 0   | Unknown                      | —                 | служебный          |
| 1   | Auth                         | C→S, S→C          | аутентификация     |
| 2   | Ping                         | C→S               | keepalive          |
| 3   | Message                      | C→S               | legacy private msg |
| 4   | Ack                          | C↔S               | надёжность         |
| 5   | Error                        | S→C               | ошибки             |
| 6   | Handshake                    | C→S, S→C          | сессия/крипто      |
| 7   | Nack                         | C↔S               | надёжность         |
| 8   | RetransmitRequest            | C→S               | надёжность         |
| 9   | UserPresence                 | C→S               | presence           |
| 10  | GroupMessage                 | C→S               | группы (legacy)    |
| 11  | GroupCreate                  | C→S               | группы             |
| 12  | GroupLeave                   | C→S               | группы             |
| 13  | ChannelMessage               | C→S               | каналы             |
| 14  | ChannelCreate                | C→S               | каналы             |
| 15  | ChannelJoin                  | C→S               | каналы             |
| 16  | ChannelLeave                 | C→S               | каналы             |
| 17  | PrivateChatMessage           | C→S               | личные сообщения   |
| 18  | UserSearch                   | C→S               | поиск              |
| 19  | UserSearchResult             | S→C               | поиск              |
| 20  | Register                     | C→S               | регистрация        |
| 21  | RegisterResponse             | S→C               | регистрация        |
| 22  | ProfileUpdate                | C→S               | профиль            |
| 23  | ProfileUpdateResponse        | S→C               | профиль            |
| 24  | ProfileGet                   | C→S               | профиль            |
| 25  | ProfileGetResponse           | S→C               | профиль            |
| 26  | MessageEdit                  | C→S               | CRUD               |
| 27  | MessageEditResponse          | S→C               | CRUD               |
| 28  | MessageDelete                | C→S               | CRUD               |
| 29  | MessageDeleteResponse        | S→C               | CRUD               |
| 30  | ChannelEdit                  | C→S               | каналы             |
| 31  | ChannelEditResponse          | S→C               | каналы             |
| 32  | GroupEdit                    | C→S               | группы             |
| 33  | GroupEditResponse            | S→C               | группы             |
| 34  | MemberRoleUpdate             | C→S               | admin              |
| 35  | MemberRoleUpdateResponse     | S→C               | admin              |
| 36  | MemberPermissionUpdate       | C→S               | admin              |
| 37  | MemberPermissionUpdateResponse| S→C              | admin              |
| 38  | GroupMessageSend             | C→S               | группы             |
| 39  | GroupMessageResponse         | S→C               | группы             |
| 40  | GroupCreateResponse          | S→C               | группы             |
| 41  | ChatListRequest              | C→S               | bootstrap          |
| 42  | ChatListResponse             | S→C               | bootstrap          |
| 43  | PrivateChatHistoryRequest    | C→S               | история            |
| 44  | PrivateChatHistoryResponse   | S→C               | история            |
| 45  | ChannelHistoryRequest        | C→S               | история            |
| 46  | ChannelHistoryResponse       | S→C               | история            |
| 47  | PrivateChatMessageEvent      | S→C               | push-событие       |
| 48  | ChannelMessageEvent          | S→C               | push-событие       |
| 49  | ProfileAvatarAdd             | C→S               | аватары            |
| 50  | ProfileAvatarAddResponse     | S→C               | аватары            |
| 51  | ProfileAvatarList            | C→S               | аватары            |
| 52  | ProfileAvatarListResponse    | S→C               | аватары            |
| 53  | ProfileAvatarDelete          | C→S               | аватары            |
| 54  | ProfileAvatarDeleteResponse  | S→C               | аватары            |
| 55  | ProfileAvatarSetPrimary      | C→S               | аватары            |
| 56  | ProfileAvatarSetPrimaryResponse| S→C             | аватары            |
| 57  | ChannelLinkUpdate            | C→S               | ссылки             |
| 58  | ChannelLinkUpdateResponse    | S→C               | ссылки             |
| 59  | ChannelLinkGet               | C→S               | ссылки             |
| 60  | ChannelLinkGetResponse       | S→C               | ссылки             |
| 61  | ChannelResolve               | C→S               | ссылки             |
| 62  | ChannelResolveResponse       | S→C               | ссылки             |
| 63  | ChannelJoinByLink            | C→S               | ссылки             |
| 64  | ChannelJoinByLinkResponse    | S→C               | ссылки             |
| 65  | MessageReadReceipt           | C→S               | статусы            |
| 66  | MessageReadReceiptResponse   | S→C               | статусы            |
| 67  | MessageDeliveryReceipt       | C→S               | статусы            |
| 68  | MessageDeliveryReceiptResponse| S→C              | статусы            |
| 69  | MessageStatusEvent           | S→C               | push-событие       |

---

## Медиавложения

Поле `Attachment` (одиночное) и `Attachments` (массив) присутствует в сообщениях personal/channel/group.

```json
{
  "FileName": "photo.jpg",
  "MimeType": "image/jpeg",
  "Base64Data": "<base64>",
  "SizeBytes": 204800
}
```

Сервер валидирует: `SizeBytes` должен соответствовать реальному размеру base64-декодированных данных. Несоответствие — reject (защита от size-bypass атак).

---

## ParseMode (форматирование текста)

Поддерживаемые значения поля `ParseMode`:

| Значение      | Описание              |
|---------------|-----------------------|
| `"markdown"`  | Базовый Markdown      |
| `"markdownv2"`| Расширенный Markdown  |
| `"html"`      | HTML-теги             |

Сервер сохраняет `ParseMode` как часть контента в структуре `StoredRichTextContent`. Рендеринг выполняет UI-клиент.

---

## Bot API

Параллельно с основным TCP-протоколом работает HTTP Bot API (отдельный сервис `Aegis.BotApi`) для создания ботов через REST-контракт. Боты взаимодействуют с сервером через внутренние сервисы, не через TCP.

---

## Конфигурация сервера (справка)

| Секция      | Параметр                          | Значение по умолчанию |
|-------------|-----------------------------------|-----------------------|
| `Server`    | `Port`                            | 8888                  |
| `Server`    | `MaxConnections`                  | 10 000                |
| `Server`    | `IdleTimeoutSeconds`              | 900                   |
| `Server`    | `EnableTransportMasking`          | true                  |
| `Server`    | `EnableIPv6`                      | true                  |
| `Tls`       | `Enabled`                         | false                 |
| `Tls`       | `CertificatePath`                 | —                     |
| `Tls`       | `CertificatePassword`             | см. env               |
| `RateLimit` | `MaxAuthAttemptsPerMinute`        | 5                     |
| `RateLimit` | `MaxMessagesPerSecond`            | 100                   |
| `RateLimit` | `MaxConnectionsPerIP`             | 10                    |
| `ProtocolSecurity` | `RequireEncryptedPayloadAfterHandshake` | true     |
| `ProtocolSecurity` | `RequireSignedHandshakeResponses`      | false    |

TLS включается через переменные окружения:
```
AEGIS_TLS__ENABLED=true
AEGIS_TLS__CERTIFICATEPATH=/certs/aegis.pfx
AEGIS_TLS__CERTIFICATEPASSWORD=<secret>
```
