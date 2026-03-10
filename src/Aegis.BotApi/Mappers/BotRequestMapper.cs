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
}
