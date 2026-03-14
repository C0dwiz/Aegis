using Aegis.BotApi.Application.Models;
using Aegis.BotApi.Contracts;
using System.Text.Json;

namespace Aegis.BotApi.Mappers;

internal static class BotResponseMapper
{
    private sealed record BotRichTextEnvelope(
        string Kind,
        string Text,
        string? ParseMode);

    private sealed record BotMediaContentEnvelope(
        string Kind,
        string? Text,
        string FileName,
        string MimeType,
        string Base64Data);

    private sealed record BotMediaAttachmentEnvelope(
        string FileName,
        string MimeType,
        long? SizeBytes = null,
        string? Base64Data = null);

    private sealed record BotMediaBatchContentEnvelope(
        string Kind,
        string? Text,
        IReadOnlyList<BotMediaAttachmentEnvelope>? Attachments);

    public static IResult ToHttpResult(UseCaseResponse<BotIdentity> response)
    {
        if (!response.Success)
        {
            return ErrorResult(response.ErrorCode, TelegramLikeResponse<GetMeResult>.Failure(response.ErrorMessage ?? "Request failed"));
        }

        var me = response.Result!;
        return Results.Ok(TelegramLikeResponse<GetMeResult>.Success(new GetMeResult(me.Name, me.UserId, me.IsBot)));
    }

    public static IResult ToHttpResult(UseCaseResponse<MessageView> response)
    {
        if (!response.Success)
        {
            return ErrorResult(response.ErrorCode, TelegramLikeResponse<MessageResult>.Failure(response.ErrorMessage ?? "Request failed"));
        }

        var message = response.Result!;
        var chatId = message.ChatKind == Domain.ChatKind.Private ? $"u:{message.ChatEntityId}" : $"c:{message.ChatEntityId}";
        var (text, fileName, mimeType, parseMode, attachments) = ExtractMessagePresentation(message.Text, message.ContentType);
        var result = new MessageResult(message.MessageId, chatId, text, message.ContentType, message.Date, fileName, mimeType, parseMode, attachments);
        return Results.Ok(TelegramLikeResponse<MessageResult>.Success(result));
    }

    public static IResult ToHttpResult(UseCaseResponse<bool> response)
    {
        if (!response.Success)
        {
            return ErrorResult(response.ErrorCode, TelegramLikeResponse<bool>.Failure(response.ErrorMessage ?? "Request failed"));
        }

        return Results.Ok(TelegramLikeResponse<bool>.Success(true));
    }

    private static IResult ErrorResult<T>(BotErrorCode? code, TelegramLikeResponse<T> payload)
    {
        return code switch
        {
            BotErrorCode.Unauthorized => Results.Unauthorized(),
            BotErrorCode.NotFound => Results.NotFound(payload),
            BotErrorCode.Validation => Results.BadRequest(payload),
            _ => Results.BadRequest(payload)
        };
    }

    private static (string Text, string? FileName, string? MimeType, string? ParseMode, IReadOnlyList<MessageAttachmentResult>? Attachments) ExtractMessagePresentation(string storedText, Aegis.Data.Entities.MessageContentType contentType)
    {
        if (contentType is not (Aegis.Data.Entities.MessageContentType.Image or Aegis.Data.Entities.MessageContentType.Video or Aegis.Data.Entities.MessageContentType.Audio or Aegis.Data.Entities.MessageContentType.File))
        {
            var rich = TryExtractRichText(storedText);
            return rich.HasValue
                ? (rich.Value.Text, null, null, rich.Value.ParseMode, null)
                : (storedText, null, null, null, null);
        }

        try
        {
            var envelope = JsonSerializer.Deserialize<BotMediaContentEnvelope>(storedText);
            if (envelope != null && string.Equals(envelope.Kind, "bot-media", StringComparison.Ordinal))
            {
                var richSingle = TryExtractRichText(envelope.Text ?? string.Empty);
                var attachmentsSingle = new[]
                {
                    new MessageAttachmentResult(envelope.FileName, envelope.MimeType, null)
                };
                return richSingle.HasValue
                    ? (richSingle.Value.Text, envelope.FileName, envelope.MimeType, richSingle.Value.ParseMode, attachmentsSingle)
                    : (envelope.Text ?? string.Empty, envelope.FileName, envelope.MimeType, null, attachmentsSingle);
            }

            var batch = JsonSerializer.Deserialize<BotMediaBatchContentEnvelope>(storedText);
            if (batch == null || !string.Equals(batch.Kind, "bot-media-batch", StringComparison.Ordinal))
            {
                return (storedText, null, null, null, null);
            }

            var mappedAttachments = (batch.Attachments ?? Array.Empty<BotMediaAttachmentEnvelope>())
                .Where(a => !string.IsNullOrWhiteSpace(a.FileName) && !string.IsNullOrWhiteSpace(a.MimeType))
                .Select(a => new MessageAttachmentResult(a.FileName, a.MimeType, a.SizeBytes))
                .ToList();

            var first = mappedAttachments.FirstOrDefault();
            var rich = TryExtractRichText(batch.Text ?? string.Empty);
            return rich.HasValue
                ? (rich.Value.Text, first?.FileName, first?.MimeType, rich.Value.ParseMode, mappedAttachments)
                : (batch.Text ?? string.Empty, first?.FileName, first?.MimeType, null, mappedAttachments);
        }
        catch
        {
            return (storedText, null, null, null, null);
        }
    }

    private static (string Text, string? ParseMode)? TryExtractRichText(string content)
    {
        try
        {
            var rich = JsonSerializer.Deserialize<BotRichTextEnvelope>(content);
            if (rich == null)
            {
                return null;
            }

            if (!string.Equals(rich.Kind, "bot-rich-text", StringComparison.Ordinal) &&
                !string.Equals(rich.Kind, "rich-text", StringComparison.Ordinal))
            {
                return null;
            }

            return (rich.Text, rich.ParseMode);
        }
        catch
        {
            return null;
        }
    }
}
