# Aegis.BotApi

HTTP/JSON API for bot-style integrations.

## Run

```bash
dotnet run --project src/Aegis.BotApi/Aegis.BotApi.csproj
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
- `POST /bot/{token}/editMessageText`
- `POST /bot/{token}/deleteMessage`

The contract is Telegram-like:

- `chat_id` (required): `u:<userId>` for private chat, `c:<channelId>` for channel.
- `text` for message body.
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
