using System.Text.Json.Serialization;
using Aegis.Data.Entities;

namespace Aegis.BotApi.Contracts;

public sealed record SendMessageRequest(
    [property: JsonPropertyName("chat_id")] string ChatId,
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("parse_mode")] string? ParseMode = null,
    [property: JsonPropertyName("reply_markup")] ReplyMarkupRequest? ReplyMarkup = null,
    [property: JsonPropertyName("photo_base64")] string? PhotoBase64 = null,
    [property: JsonPropertyName("file_base64")] string? FileBase64 = null,
    [property: JsonPropertyName("file_name")] string? FileName = null,
    [property: JsonPropertyName("mime_type")] string? MimeType = null,
    MessageContentType ContentType = MessageContentType.Text,
    [property: JsonPropertyName("disable_web_page_preview")] bool DisableWebPagePreview = false);

public sealed record SendPhotoRequest(
    [property: JsonPropertyName("chat_id")] string ChatId,
    [property: JsonPropertyName("photo_base64")] string PhotoBase64,
    [property: JsonPropertyName("caption")] string? Caption = null,
    [property: JsonPropertyName("parse_mode")] string? ParseMode = null,
    [property: JsonPropertyName("reply_markup")] ReplyMarkupRequest? ReplyMarkup = null,
    [property: JsonPropertyName("file_name")] string? FileName = null,
    [property: JsonPropertyName("mime_type")] string? MimeType = null);

public sealed record SendDocumentRequest(
    [property: JsonPropertyName("chat_id")] string ChatId,
    [property: JsonPropertyName("file_base64")] string FileBase64,
    [property: JsonPropertyName("caption")] string? Caption = null,
    [property: JsonPropertyName("parse_mode")] string? ParseMode = null,
    [property: JsonPropertyName("reply_markup")] ReplyMarkupRequest? ReplyMarkup = null,
    [property: JsonPropertyName("file_name")] string? FileName = null,
    [property: JsonPropertyName("mime_type")] string? MimeType = null);

public sealed record SendMediaRequest(
    [property: JsonPropertyName("chat_id")] string ChatId,
    [property: JsonPropertyName("media_base64")] string MediaBase64,
    [property: JsonPropertyName("caption")] string? Caption = null,
    [property: JsonPropertyName("parse_mode")] string? ParseMode = null,
    [property: JsonPropertyName("reply_markup")] ReplyMarkupRequest? ReplyMarkup = null,
    [property: JsonPropertyName("file_name")] string? FileName = null,
    [property: JsonPropertyName("mime_type")] string? MimeType = null,
    [property: JsonPropertyName("content_type")] MessageContentType? ContentType = null);

public sealed record SendFileRequest(
    [property: JsonPropertyName("chat_id")] string ChatId,
    [property: JsonPropertyName("file_base64")] string FileBase64,
    [property: JsonPropertyName("caption")] string? Caption = null,
    [property: JsonPropertyName("parse_mode")] string? ParseMode = null,
    [property: JsonPropertyName("reply_markup")] ReplyMarkupRequest? ReplyMarkup = null,
    [property: JsonPropertyName("file_name")] string? FileName = null,
    [property: JsonPropertyName("mime_type")] string? MimeType = null);

public sealed record SendVoiceMessageRequest(
    [property: JsonPropertyName("chat_id")] string ChatId,
    [property: JsonPropertyName("voice_base64")] string VoiceBase64,
    [property: JsonPropertyName("caption")] string? Caption = null,
    [property: JsonPropertyName("parse_mode")] string? ParseMode = null,
    [property: JsonPropertyName("reply_markup")] ReplyMarkupRequest? ReplyMarkup = null,
    [property: JsonPropertyName("file_name")] string? FileName = null,
    [property: JsonPropertyName("mime_type")] string? MimeType = null);

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
