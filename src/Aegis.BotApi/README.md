# Aegis.BotApi

HTTP/JSON API for bot-style integrations.

## Run

### Local

```bash
dotnet run --project src/Aegis.BotApi/Aegis.BotApi.csproj
```

### Docker Compose (recommended)

From repository root:

```bash
docker compose up --build -d
```

Swagger UI: `http://localhost:5000/swagger`

## Configure bots

Edit `src/Aegis.BotApi/appsettings.json`:

```json
"BotApi": {
  "Bots": [
    {
      "Name": "demo-bot",
      "Token": "CHANGE_ME_BOT_TOKEN",
      "UserId": 1
    }
  ]
}
```

`UserId` is the sender identity for bot messages.

## Endpoints

- `GET /bot/{token}/getMe`
- `POST /bot/{token}/sendMessage`
- `POST /bot/{token}/sendPhoto`
- `POST /bot/{token}/sendDocument`
- `POST /bot/{token}/sendMedia`
- `POST /bot/{token}/sendFile`
- `POST /bot/{token}/sendVoiceMessage`
- `POST /bot/{token}/editMessageText`
- `POST /bot/{token}/deleteMessage`

The contract is Telegram-like:

- `chat_id` (required): `u:<userId>` for private chat, `c:<channelId>` for channel.
- `text` for message body.
- `photo_base64` to send an image (base64 payload).
- `file_base64` to send any file (base64 payload).
- `file_name`, `mime_type` optional metadata for media payload.
- `reply_markup.inline_keyboard` for inline buttons.
- `parse_mode` optional (`Markdown`, `HTML`, etc.).

## Request examples

### sendMessage

```json
{
  "chat_id": "u:2",
  "text": "hello from bot",
  "parse_mode": "Markdown",
  "reply_markup": {
    "inline_keyboard": [
      [
        { "text": "Open", "callback_data": "open", "url": "https://example.com" },
        { "text": "Cancel", "callback_data": "cancel" }
      ]
    ]
  }
}
```

### sendMessage with photo

```json
{
  "chat_id": "u:2",
  "text": "Photo caption",
  "photo_base64": "<base64-image>",
  "file_name": "photo.jpg",
  "mime_type": "image/jpeg"
}
```

### sendMessage with file

```json
{
  "chat_id": "u:2",
  "text": "Document",
  "file_base64": "<base64-file>",
  "file_name": "report.pdf",
  "mime_type": "application/pdf"
}
```

### sendPhoto

```json
{
  "chat_id": "u:2",
  "photo_base64": "<base64-image>",
  "caption": "Photo caption",
  "file_name": "photo.jpg",
  "mime_type": "image/jpeg"
}
```

### sendDocument

```json
{
  "chat_id": "u:2",
  "file_base64": "<base64-file>",
  "caption": "Quarterly report",
  "file_name": "report.pdf",
  "mime_type": "application/pdf"
}
```

### sendMedia (photo/video/gif)

```json
{
  "chat_id": "u:2",
  "media_base64": "<base64-media>",
  "caption": "video/gif/photo",
  "file_name": "clip.mp4",
  "mime_type": "video/mp4"
}
```

For GIF use `mime_type: image/gif`.

### sendFile

```json
{
  "chat_id": "u:2",
  "file_base64": "<base64-file>",
  "caption": "archive",
  "file_name": "backup.zip",
  "mime_type": "application/zip"
}
```

### sendVoiceMessage

```json
{
  "chat_id": "u:2",
  "voice_base64": "<base64-ogg>",
  "caption": "voice note",
  "file_name": "voice.ogg",
  "mime_type": "audio/ogg"
}
```

or channel:

```json
{
  "chat_id": "c:10",
  "text": "hello channel"
}
```

### editMessageText

```json
{
  "chat_id": "u:2",
  "message_id": 123,
  "text": "updated text",
  "reply_markup": {
    "inline_keyboard": [
      [
        { "text": "Confirm", "callback_data": "confirm" }
      ]
    ]
  }
}
```

### deleteMessage

```json
{
  "chat_id": "u:2",
  "message_id": 123
}
```
