using System.Text.Json.Serialization;
using Aegis.Data.Entities;

namespace Aegis.BotApi.Contracts;

public sealed record SendMessageRequest(
    [property: JsonPropertyName("chat_id")] string ChatId,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("parse_mode")] string? ParseMode = null,
    [property: JsonPropertyName("reply_markup")] ReplyMarkupRequest? ReplyMarkup = null,
    MessageContentType ContentType = MessageContentType.Text,
    [property: JsonPropertyName("disable_web_page_preview")] bool DisableWebPagePreview = false);

public sealed record EditMessageTextRequest(
    [property: JsonPropertyName("chat_id")] string ChatId,
    [property: JsonPropertyName("message_id")] ulong MessageId,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("parse_mode")] string? ParseMode = null,
    [property: JsonPropertyName("reply_markup")] ReplyMarkupRequest? ReplyMarkup = null,
    MessageContentType ContentType = MessageContentType.Text);

public sealed record DeleteMessageRequest(
    [property: JsonPropertyName("chat_id")] string ChatId,
    [property: JsonPropertyName("message_id")] ulong MessageId);

public sealed record ReplyMarkupRequest(
    [property: JsonPropertyName("inline_keyboard")] List<List<InlineKeyboardButtonRequest>> InlineKeyboard);

public sealed record InlineKeyboardButtonRequest(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("callback_data")] string? CallbackData = null,
    [property: JsonPropertyName("url")] string? Url = null);
