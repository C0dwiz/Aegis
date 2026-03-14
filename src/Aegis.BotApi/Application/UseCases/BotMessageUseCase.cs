using Aegis.BotApi.Application.Abstractions;
using Aegis.BotApi.Application.Models;
using Aegis.BotApi.Domain;
using Aegis.BotApi.Infrastructure.Auth;
using Aegis.Data.Services;
using Aegis.Data.Entities;
using Aegis.Data.Policies;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Aegis.BotApi.Application.UseCases;

internal sealed class BotMessageUseCase : IBotMessageUseCase
{
    private sealed record BotMediaAttachmentEnvelope(
        string FileName,
        string MimeType,
        string Base64Data,
        long? SizeBytes = null);

    private sealed record BotMediaEnvelope(
        string Kind,
        string? Text,
        string FileName,
        string MimeType,
        string Base64Data);

    private sealed record BotMediaBatchEnvelope(
        string Kind,
        string? Text,
        IReadOnlyList<BotMediaAttachmentEnvelope>? Attachments);

    private const int MaxMediaPayloadBytes = MediaPolicy.MaxSingleAttachmentBytes;
    private const int MaxBatchAttachments = MediaPolicy.MaxAttachmentsPerMessage;
    private const int MaxBatchPayloadBytes = MediaPolicy.MaxTotalAttachmentsBytes;

    private readonly IBotAuthenticator _authenticator;
    private readonly IMessageService _messageService;
    private readonly IUserSearchService _userSearchService;
    private readonly ILogger<BotMessageUseCase> _logger;

    public BotMessageUseCase(
        IBotAuthenticator authenticator,
        IMessageService messageService,
        IUserSearchService userSearchService,
        ILogger<BotMessageUseCase> logger)
    {
        _authenticator = authenticator;
        _messageService = messageService;
        _userSearchService = userSearchService;
        _logger = logger;
    }

    public async Task<UseCaseResponse<BotIdentity>> GetMeAsync(string token)
    {
        var bot = await _authenticator.AuthenticateAsync(token);
        if (bot == null)
        {
            return UseCaseResponse<BotIdentity>.Fail(BotErrorCode.Unauthorized, "Unauthorized token or bot not found");
        }

        return UseCaseResponse<BotIdentity>.Ok(bot);
    }

    public async Task<UseCaseResponse<MessageView>> SendMessageAsync(string token, SendMessageCommand command)
    {
        try
        {
            var bot = await _authenticator.AuthenticateAsync(token);
            if (bot == null)
            {
                return UseCaseResponse<MessageView>.Fail(BotErrorCode.Unauthorized, "Unauthorized token or bot not found");
            }

            if (!command.Chat.IsValid)
            {
                return UseCaseResponse<MessageView>.Fail(BotErrorCode.Validation, command.Chat.Error ?? "Invalid chat");
            }

            var validationError = ValidateContent(command.Content, command.ContentType);
            if (validationError != null)
            {
                return UseCaseResponse<MessageView>.Fail(BotErrorCode.Validation, validationError);
            }

            if (command.Chat.Kind == ChatKind.Private)
            {
                var targetUser = await _userSearchService.FindUserByIdAsync(command.Chat.EntityId);
                if (targetUser == null)
                {
                    return UseCaseResponse<MessageView>.Fail(BotErrorCode.NotFound, "Target user not found");
                }

                var message = await _messageService.SendPrivateMessageAsync(
                    bot.UserId,
                    command.Chat.EntityId,
                    command.Content,
                    command.ContentType);

                return UseCaseResponse<MessageView>.Ok(new MessageView(
                    message.Id,
                    command.Chat.Kind,
                    command.Chat.EntityId,
                    message.Content,
                    message.ContentType,
                    message.CreatedAt));
            }

            var channelMessage = await _messageService.SendChannelMessageAsync(
                command.Chat.EntityId,
                bot.UserId,
                command.Content,
                command.ContentType);

            return UseCaseResponse<MessageView>.Ok(new MessageView(
                channelMessage.Id,
                ChatKind.Channel,
                command.Chat.EntityId,
                channelMessage.Content,
                channelMessage.ContentType,
                channelMessage.CreatedAt));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send bot message. chatKind={ChatKind}, chatId={ChatId}, contentType={ContentType}",
                command.Chat.Kind,
                command.Chat.EntityId,
                command.ContentType);
            return UseCaseResponse<MessageView>.Fail(BotErrorCode.Failed, "Failed to send message");
        }
    }

    public async Task<UseCaseResponse<MessageView>> EditMessageTextAsync(string token, EditMessageCommand command)
    {
        var bot = await _authenticator.AuthenticateAsync(token);
        if (bot == null)
        {
            return UseCaseResponse<MessageView>.Fail(BotErrorCode.Unauthorized, "Unauthorized token or bot not found");
        }

        if (!command.Chat.IsValid)
        {
            return UseCaseResponse<MessageView>.Fail(BotErrorCode.Validation, command.Chat.Error ?? "Invalid chat");
        }

        try
        {
            if (command.Chat.Kind == ChatKind.Channel)
            {
                var updated = await _messageService.EditChannelMessageAsync(
                    command.MessageId,
                    bot.UserId,
                    command.Chat.EntityId,
                    command.Content);

                return UseCaseResponse<MessageView>.Ok(new MessageView(
                    updated.Id,
                    command.Chat.Kind,
                    command.Chat.EntityId,
                    updated.Content,
                    updated.ContentType,
                    updated.CreatedAt));
            }

            var edited = await _messageService.EditMessageAsync(command.MessageId, bot.UserId, command.Content);
            return UseCaseResponse<MessageView>.Ok(new MessageView(
                edited.Id,
                command.Chat.Kind,
                command.Chat.EntityId,
                edited.Content,
                edited.ContentType,
                edited.CreatedAt));
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            return UseCaseResponse<MessageView>.Fail(BotErrorCode.Failed, ex.Message);
        }
    }

    public async Task<UseCaseResponse<bool>> DeleteMessageAsync(string token, DeleteMessageCommand command)
    {
        var bot = await _authenticator.AuthenticateAsync(token);
        if (bot == null)
        {
            return UseCaseResponse<bool>.Fail(BotErrorCode.Unauthorized, "Unauthorized token or bot not found");
        }

        if (!command.Chat.IsValid)
        {
            return UseCaseResponse<bool>.Fail(BotErrorCode.Validation, command.Chat.Error ?? "Invalid chat");
        }

        var deleted = command.Chat.Kind == ChatKind.Channel
            ? await _messageService.DeleteChannelMessageAsync(command.MessageId, bot.UserId, command.Chat.EntityId)
            : await _messageService.DeleteMessageAsync(command.MessageId, bot.UserId);

        return deleted
            ? UseCaseResponse<bool>.Ok(true)
            : UseCaseResponse<bool>.Fail(BotErrorCode.Failed, "Message was not deleted");
    }

    private static string? ValidateContent(string content, MessageContentType contentType)
    {
        if (contentType == MessageContentType.Text)
        {
            return string.IsNullOrWhiteSpace(content)
                ? "Text message cannot be empty"
                : null;
        }

        if (contentType is not (MessageContentType.Image or MessageContentType.Video or MessageContentType.Audio or MessageContentType.File))
        {
            return null;
        }

        BotMediaEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<BotMediaEnvelope>(content);
        }
        catch
        {
            return "Invalid media payload format";
        }

        if (envelope == null)
        {
            return "Invalid media payload envelope";
        }

        if (string.Equals(envelope.Kind, "bot-media", StringComparison.Ordinal))
        {
            return ValidateSingleMedia(envelope.Base64Data);
        }

        if (string.Equals(envelope.Kind, "bot-media-batch", StringComparison.Ordinal))
        {
            BotMediaBatchEnvelope? batchEnvelope;
            try
            {
                batchEnvelope = JsonSerializer.Deserialize<BotMediaBatchEnvelope>(content);
            }
            catch
            {
                return "Invalid media payload envelope";
            }

            if (batchEnvelope?.Attachments == null || batchEnvelope.Attachments.Count == 0)
            {
                return "Media batch must include at least one attachment";
            }

            if (batchEnvelope.Attachments.Count > MaxBatchAttachments)
            {
                return $"Media batch supports up to {MaxBatchAttachments} attachments";
            }

            var totalBytes = 0;
            foreach (var attachment in batchEnvelope.Attachments)
            {
                if (string.IsNullOrWhiteSpace(attachment.Base64Data))
                {
                    return "Media payload is empty";
                }

                try
                {
                    var bytes = Convert.FromBase64String(attachment.Base64Data);
                    if (bytes.Length == 0)
                    {
                        return "Media payload is empty";
                    }

                    if (bytes.Length > MaxMediaPayloadBytes)
                    {
                        return $"Media payload exceeds {MaxMediaPayloadBytes / 1024}KB limit";
                    }

                    totalBytes += bytes.Length;
                    if (totalBytes > MaxBatchPayloadBytes)
                    {
                        return $"Media batch exceeds {MaxBatchPayloadBytes / 1024}KB total limit";
                    }
                }
                catch
                {
                    return "Media payload must be valid base64";
                }
            }

            return null;
        }

        return "Invalid media payload envelope";
    }

    private static string? ValidateSingleMedia(string base64Data)
    {
        if (string.IsNullOrWhiteSpace(base64Data))
        {
            return "Media payload is empty";
        }

        try
        {
            var bytes = Convert.FromBase64String(base64Data);
            if (bytes.Length == 0)
            {
                return "Media payload is empty";
            }

            if (bytes.Length > MaxMediaPayloadBytes)
            {
                return $"Media payload exceeds {MaxMediaPayloadBytes / 1024}KB limit";
            }
        }
        catch
        {
            return "Media payload must be valid base64";
        }

        return null;
    }
}
