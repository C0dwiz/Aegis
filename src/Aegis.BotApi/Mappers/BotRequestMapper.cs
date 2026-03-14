using Aegis.BotApi.Contracts;
using Aegis.BotApi.Domain;
using Aegis.BotApi.Services;
using Aegis.Data.Entities;
using System.Text.Json;

namespace Aegis.BotApi.Mappers;

internal sealed class BotRequestMapper
{
    private sealed record BotMediaContentEnvelope(
        string Kind,
        string? Text,
        string FileName,
        string MimeType,
        string Base64Data);

    private sealed record BotMediaAttachmentEnvelope(
        string FileName,
        string MimeType,
        string Base64Data,
        long? SizeBytes = null);

    private sealed record BotMediaBatchContentEnvelope(
        string Kind,
        string? Text,
        IReadOnlyList<BotMediaAttachmentEnvelope> Attachments);

    private readonly ChatIdResolver _chatIdResolver;
    private readonly RichContentFormatter _contentFormatter;

    public BotRequestMapper(ChatIdResolver chatIdResolver, RichContentFormatter contentFormatter)
    {
        _chatIdResolver = chatIdResolver;
        _contentFormatter = contentFormatter;
    }

    public SendMessageCommand Map(SendMessageRequest request)
    {
        var resolved = _chatIdResolver.Resolve(request.ChatId);
        var hasPhoto = !string.IsNullOrWhiteSpace(request.PhotoBase64);
        var hasFile = !string.IsNullOrWhiteSpace(request.FileBase64);

        if (hasPhoto || hasFile)
        {
            var base64 = hasPhoto ? request.PhotoBase64! : request.FileBase64!;
            var mimeType = request.MimeType
                ?? (hasPhoto ? "image/jpeg" : "application/octet-stream");
            var fileName = request.FileName
                ?? (hasPhoto ? "photo.jpg" : "file.bin");

            var envelope = new BotMediaContentEnvelope(
                Kind: "bot-media",
                Text: request.Text,
                FileName: fileName,
                MimeType: mimeType,
                Base64Data: base64);

            var content = JsonSerializer.Serialize(envelope);
            var contentType = hasPhoto ? MessageContentType.Image : MessageContentType.File;
            return new SendMessageCommand(resolved, content, contentType);
        }

        var contentText = _contentFormatter.BuildContent(request.Text ?? string.Empty, request.ParseMode, request.ReplyMarkup);
        return new SendMessageCommand(resolved, contentText, request.ContentType);
    }

    public SendMessageCommand Map(SendPhotoRequest request)
    {
        var sendMessageRequest = new SendMessageRequest(
            ChatId: request.ChatId,
            Text: request.Caption,
            ParseMode: request.ParseMode,
            ReplyMarkup: request.ReplyMarkup,
            PhotoBase64: request.PhotoBase64,
            FileBase64: null,
            FileName: request.FileName,
            MimeType: request.MimeType,
            ContentType: MessageContentType.Image);

        return Map(sendMessageRequest);
    }

    public SendMessageCommand Map(SendDocumentRequest request)
    {
        var sendMessageRequest = new SendMessageRequest(
            ChatId: request.ChatId,
            Text: request.Caption,
            ParseMode: request.ParseMode,
            ReplyMarkup: request.ReplyMarkup,
            PhotoBase64: null,
            FileBase64: request.FileBase64,
            FileName: request.FileName,
            MimeType: request.MimeType,
            ContentType: MessageContentType.File);

        return Map(sendMessageRequest);
    }

    public SendMessageCommand Map(SendMediaRequest request)
    {
        var contentType = request.ContentType ?? InferContentTypeByMime(request.MimeType);

        return BuildMediaCommand(
            request.ChatId,
            request.MediaBase64,
            request.Caption,
            request.ParseMode,
            request.ReplyMarkup,
            request.FileName,
            request.MimeType,
            contentType);
    }

    public SendMessageCommand Map(SendFileRequest request)
    {
        return BuildMediaCommand(
            request.ChatId,
            request.FileBase64,
            request.Caption,
            request.ParseMode,
            request.ReplyMarkup,
            request.FileName,
            request.MimeType,
            MessageContentType.File);
    }

    public SendMessageCommand Map(SendVoiceMessageRequest request)
    {
        return BuildMediaCommand(
            request.ChatId,
            request.VoiceBase64,
            request.Caption,
            request.ParseMode,
            request.ReplyMarkup,
            request.FileName ?? "voice.ogg",
            request.MimeType ?? "audio/ogg",
            MessageContentType.Audio);
    }

    public SendMessageCommand Map(SendMediaBatchRequest request)
    {
        var normalizedText = _contentFormatter.BuildContent(request.Caption ?? string.Empty, request.ParseMode, request.ReplyMarkup);
        var normalizedAttachments = (request.Attachments ?? Array.Empty<BotMediaAttachmentRequest>())
            .Select(a => new BotMediaAttachmentEnvelope(
                FileName: string.IsNullOrWhiteSpace(a.FileName) ? "file.bin" : a.FileName,
                MimeType: string.IsNullOrWhiteSpace(a.MimeType) ? "application/octet-stream" : a.MimeType,
                Base64Data: a.Base64Data,
                SizeBytes: a.SizeBytes))
            .ToList();

        var resolved = _chatIdResolver.Resolve(request.ChatId);
        var contentType = request.ContentType ?? InferContentTypeForBatch(normalizedAttachments);

        var envelope = new BotMediaBatchContentEnvelope(
            Kind: "bot-media-batch",
            Text: normalizedText,
            Attachments: normalizedAttachments);

        var content = JsonSerializer.Serialize(envelope);
        return new SendMessageCommand(resolved, content, contentType);
    }

    public EditMessageCommand Map(EditMessageTextRequest request)
    {
        var resolved = _chatIdResolver.Resolve(request.ChatId);
        var content = _contentFormatter.BuildContent(request.Text, request.ParseMode, request.ReplyMarkup);
        return new EditMessageCommand(resolved, request.MessageId, content, request.ContentType);
    }

    public DeleteMessageCommand Map(DeleteMessageRequest request)
    {
        var resolved = _chatIdResolver.Resolve(request.ChatId);
        return new DeleteMessageCommand(resolved, request.MessageId);
    }

    private SendMessageCommand BuildMediaCommand(
        string chatId,
        string base64,
        string? caption,
        string? parseMode,
        ReplyMarkupRequest? replyMarkup,
        string? fileName,
        string? mimeType,
        MessageContentType contentType)
    {
        var resolved = _chatIdResolver.Resolve(chatId);
        var normalizedMimeType = string.IsNullOrWhiteSpace(mimeType)
            ? DefaultMime(contentType)
            : mimeType!;
        var normalizedFileName = string.IsNullOrWhiteSpace(fileName)
            ? DefaultFileName(contentType)
            : fileName!;
        var normalizedText = _contentFormatter.BuildContent(caption ?? string.Empty, parseMode, replyMarkup);

        var envelope = new BotMediaContentEnvelope(
            Kind: "bot-media",
            Text: normalizedText,
            FileName: normalizedFileName,
            MimeType: normalizedMimeType,
            Base64Data: base64);

        var content = JsonSerializer.Serialize(envelope);
        return new SendMessageCommand(resolved, content, contentType);
    }

    private static MessageContentType InferContentTypeByMime(string? mimeType)
    {
        if (string.IsNullOrWhiteSpace(mimeType))
        {
            return MessageContentType.File;
        }

        if (mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return MessageContentType.Image;
        }

        if (mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
        {
            return MessageContentType.Video;
        }

        if (mimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
        {
            return MessageContentType.Audio;
        }

        return MessageContentType.File;
    }

    private static MessageContentType InferContentTypeForBatch(IReadOnlyList<BotMediaAttachmentEnvelope> attachments)
    {
        if (attachments.Count == 0)
        {
            return MessageContentType.File;
        }

        if (attachments.All(a => a.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)))
        {
            return MessageContentType.Image;
        }

        if (attachments.All(a => a.MimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)))
        {
            return MessageContentType.Video;
        }

        if (attachments.All(a => a.MimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)))
        {
            return MessageContentType.Audio;
        }

        return MessageContentType.File;
    }

    private static string DefaultFileName(MessageContentType contentType) => contentType switch
    {
        MessageContentType.Image => "photo.jpg",
        MessageContentType.Video => "video.mp4",
        MessageContentType.Audio => "voice.ogg",
        _ => "file.bin"
    };

    private static string DefaultMime(MessageContentType contentType) => contentType switch
    {
        MessageContentType.Image => "image/jpeg",
        MessageContentType.Video => "video/mp4",
        MessageContentType.Audio => "audio/ogg",
        _ => "application/octet-stream"
    };
}
