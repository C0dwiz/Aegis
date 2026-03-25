using System.Text.Json;
using Aegis.Common;
using Aegis.Data.Entities;
using Aegis.Data.Policies;
using Aegis.Data.Repositories;
using Aegis.Data.Services;
using Aegis.Protocol;
using Aegis.Transport;
using Microsoft.Extensions.Logging;

namespace Aegis.Handlers;

/// <summary>
/// Channel message request payload
/// </summary>
public record ChannelMessageRequest(
    ulong ChannelId,
    string? Content,
    MessageContentType ContentType = MessageContentType.Text,
    ulong? ReplyToMessageId = null,
    MediaAttachmentPayload? Attachment = null,
    IReadOnlyList<MediaAttachmentPayload>? Attachments = null,
    string? ParseMode = null
);

/// <summary>
/// Channel message response payload
/// </summary>
public record ChannelMessageResponse(
    bool Success,
    ulong MessageId = 0,
    string? MessageText = null
);

/// <summary>
/// Channel create request payload
/// </summary>
public record ChannelCreateRequest(
    string Name,
    string? Description = null,
    ChannelType Type = ChannelType.Public
);

/// <summary>
/// Channel create response payload
/// </summary>
public record ChannelCreateResponse(
    bool Success,
    ulong ChannelId = 0,
    string? Message = null
);

/// <summary>
/// Channel join request payload
/// </summary>
public record ChannelJoinRequest(
    ulong ChannelId
);

/// <summary>
/// Channel join response payload
/// </summary>
public record ChannelJoinResponse(
    bool Success,
    ChannelSummary? Channel = null,
    string? Message = null
);

/// <summary>
/// Minimal channel info returned in join/create responses
/// </summary>
public record ChannelSummary(
    ulong Id,
    string Name,
    string? Description,
    int Type,
    int MemberCount
);

/// <summary>
/// Private chat message request payload
/// </summary>
public record PrivateChatMessageRequest(
    ulong ToUserId,
    string? Content,
    MessageContentType ContentType = MessageContentType.Text,
    MediaAttachmentPayload? Attachment = null,
    IReadOnlyList<MediaAttachmentPayload>? Attachments = null,
    string? ParseMode = null
);

internal sealed record StoredRichTextContent(
    string Kind,
    string Text,
    string ParseMode
);

public record MediaAttachmentPayload(
    string FileName,
    string MimeType,
    string Base64Data,
    long? SizeBytes = null
);

internal sealed record StoredMediaContent(
    string? Text,
    string FileName,
    string MimeType,
    string Base64Data,
    long? SizeBytes
);

internal sealed record StoredMediaBatchContent(
    string Kind,
    string? Text,
    IReadOnlyList<MediaAttachmentPayload> Attachments
);

/// <summary>
/// Private chat message response payload
/// </summary>
public record PrivateChatMessageResponse(
    bool Success,
    ulong MessageId = 0,
    string? MessageText = null
);

// ===================== CHANNEL MESSAGE HANDLER =====================

public class ChannelMessageHandler : IMessageHandler
{
    public MessageType Type => MessageType.ChannelMessage;
    
    private readonly IMessageService _messageService;
    private readonly IChannelRepository _channelRepository;
    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;
    private readonly DomainRulesAdapter _domainRules;
    private readonly ILogger<ChannelMessageHandler> _logger;

    public ChannelMessageHandler(
        IMessageService messageService,
        IChannelRepository channelRepository,
        SessionManager sessionManager,
        IMessageSender messageSender,
        DomainRulesAdapter domainRules,
        ILogger<ChannelMessageHandler> logger)
    {
        _messageService = messageService;
        _channelRepository = channelRepository;
        _sessionManager = sessionManager;
        _messageSender = messageSender;
        _domainRules = domainRules;
        _logger = logger;
    }

    public async ValueTask HandleAsync(ConnectionContext context, Aegis.Protocol.Message message)
    {
        try
        {
            var session = _sessionManager.GetAuthenticatedSession(context.ConnectionId);
            if (session == null)
            {
                await SendResponseAsync(context, message.SequenceId, new ChannelMessageResponse(false, MessageText: "Not authenticated"));
                return;
            }

            var payload = PayloadSerializer.Deserialize<ChannelMessageRequest>(message.Payload);
            if (payload == null)
            {
                await SendResponseAsync(context, message.SequenceId, new ChannelMessageResponse(false, MessageText: "Invalid payload"));
                return;
            }

            if (!_domainRules.TryValidateMessageSend(
                scope: "channel",
                targetId: payload.ChannelId,
                senderUserId: session.UserId,
                content: payload.Content,
                attachmentCount: MediaPayloadBuilder.GetNormalizedAttachmentCount(payload.Attachment, payload.Attachments),
                requestedContentType: (int)payload.ContentType,
                out var ruleError))
            {
                await SendResponseAsync(context, message.SequenceId, new ChannelMessageResponse(false, MessageText: ruleError ?? "Message violates domain rules"));
                return;
            }

            var contentType = MediaPayloadBuilder.ResolveContentType(payload.ContentType, payload.Attachment, payload.Attachments);
            var normalizedContent = MediaPayloadBuilder.BuildMessageContent(payload.Content, payload.Attachment, payload.Attachments, payload.ParseMode);

            var channelMsg = await _messageService.SendChannelMessageAsync(
                payload.ChannelId, session.UserId, normalizedContent,
                contentType, payload.ReplyToMessageId);

            var channel = await _channelRepository.GetByIdAsync(payload.ChannelId);
            var channelMembers = await _channelRepository.GetChannelMembersAsync(payload.ChannelId);

            var eventPayload = PayloadSerializer.Serialize(new ChannelMessageEventPayload(
                Id: channelMsg.Id,
                ChannelId: payload.ChannelId,
                FromUserId: session.UserId,
                Content: channelMsg.Content,
                ContentType: channelMsg.ContentType,
                CreatedAt: channelMsg.CreatedAt,
                DeliveredTo: channelMsg.IsDelivered ? [session.UserId] : [],
                ReadBy: channelMsg.IsRead ? [session.UserId] : [],
                FromUsername: session.Username,
                ChannelName: channel?.Name));

            foreach (var member in channelMembers)
            {
                if (member.UserId == session.UserId)
                {
                    continue;
                }

                if (!_sessionManager.TryGetConnectionIdByUserId(member.UserId, out var recipientConnectionId))
                {
                    continue;
                }

                await _messageSender.SendProtocolMessageAsync(
                    recipientConnectionId,
                    (ushort)MessageType.ChannelMessageEvent,
                    0,
                    eventPayload);
            }

            await SendResponseAsync(context, message.SequenceId, 
                new ChannelMessageResponse(true, channelMsg.Id, "Message sent"));

            _logger.LogInformation("Channel message sent to channel {ChannelId} by user {UserId}", 
                payload.ChannelId, session.UserId);
        }
        catch (UnauthorizedAccessException ex)
        {
            await SendResponseAsync(context, message.SequenceId, new ChannelMessageResponse(false, MessageText: ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            await SendResponseAsync(context, message.SequenceId, new ChannelMessageResponse(false, MessageText: ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogHandlerError(ex, "channel_message_send", context, message, _sessionManager);
            await SendResponseAsync(context, message.SequenceId, new ChannelMessageResponse(false, MessageText: "Internal server error"));
        }
    }

    private async Task SendResponseAsync(ConnectionContext context, ulong sequenceId, ChannelMessageResponse response)
    {
        await HandlerResponseSender.SendAsync(
            _messageSender,
            context,
            MessageType.ChannelMessage,
            sequenceId,
            response);
    }
}

// ===================== CHANNEL CREATE HANDLER =====================

public class ChannelCreateHandler : IMessageHandler
{
    public MessageType Type => MessageType.ChannelCreate;
    
    private readonly IChannelService _channelService;
    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;
    private readonly ILogger<ChannelCreateHandler> _logger;

    public ChannelCreateHandler(
        IChannelService channelService,
        SessionManager sessionManager,
        IMessageSender messageSender,
        ILogger<ChannelCreateHandler> logger)
    {
        _channelService = channelService;
        _sessionManager = sessionManager;
        _messageSender = messageSender;
        _logger = logger;
    }

    public async ValueTask HandleAsync(ConnectionContext context, Aegis.Protocol.Message message)
    {
        try
        {
            var session = _sessionManager.GetAuthenticatedSession(context.ConnectionId);
            if (session == null)
            {
                await SendResponseAsync(context, message.SequenceId, new ChannelCreateResponse(false, Message: "Not authenticated"));
                return;
            }

            var payload = PayloadSerializer.Deserialize<ChannelCreateRequest>(message.Payload);
            if (payload == null)
            {
                await SendResponseAsync(context, message.SequenceId, new ChannelCreateResponse(false, Message: "Invalid payload"));
                return;
            }

            var channel = await _channelService.CreateChannelAsync(
                session.UserId, payload.Name, payload.Description, payload.Type);

            await SendResponseAsync(context, message.SequenceId, 
                new ChannelCreateResponse(true, channel.Id, "Channel created"));

            _logger.LogInformation("Channel '{ChannelName}' created by user {UserId}", payload.Name, session.UserId);
        }
        catch (ArgumentException ex)
        {
            await SendResponseAsync(context, message.SequenceId, new ChannelCreateResponse(false, Message: ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogHandlerError(ex, "channel_create", context, message, _sessionManager);
            await SendResponseAsync(context, message.SequenceId, new ChannelCreateResponse(false, Message: "Internal server error"));
        }
    }

    private async Task SendResponseAsync(ConnectionContext context, ulong sequenceId, ChannelCreateResponse response)
    {
        await HandlerResponseSender.SendAsync(
            _messageSender,
            context,
            MessageType.ChannelCreate,
            sequenceId,
            response);
    }
}

// ===================== PRIVATE CHAT MESSAGE HANDLER =====================

public class PrivateChatMessageHandler : IMessageHandler
{
    public MessageType Type => MessageType.PrivateChatMessage;
    
    private readonly IMessageService _messageService;
    private readonly IUserSearchService _userSearchService;
    private readonly IBotManagementService _botManagementService;
    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;
    private readonly DomainRulesAdapter _domainRules;
    private readonly ILogger<PrivateChatMessageHandler> _logger;

    public PrivateChatMessageHandler(
        IMessageService messageService,
        IUserSearchService userSearchService,
        IBotManagementService botManagementService,
        SessionManager sessionManager,
        IMessageSender messageSender,
        DomainRulesAdapter domainRules,
        ILogger<PrivateChatMessageHandler> logger)
    {
        _messageService = messageService;
        _userSearchService = userSearchService;
        _botManagementService = botManagementService;
        _sessionManager = sessionManager;
        _messageSender = messageSender;
        _domainRules = domainRules;
        _logger = logger;
    }

    public async ValueTask HandleAsync(ConnectionContext context, Aegis.Protocol.Message message)
    {
        try
        {
            var session = _sessionManager.GetAuthenticatedSession(context.ConnectionId);
            if (session == null)
            {
                await SendResponseAsync(context, message.SequenceId, new PrivateChatMessageResponse(false, MessageText: "Not authenticated"));
                return;
            }

            var payload = PayloadSerializer.Deserialize<PrivateChatMessageRequest>(message.Payload);
            if (payload == null)
            {
                await SendResponseAsync(context, message.SequenceId, new PrivateChatMessageResponse(false, MessageText: "Invalid payload"));
                return;
            }

            if (!_domainRules.TryValidateMessageSend(
                scope: "private",
                targetId: payload.ToUserId,
                senderUserId: session.UserId,
                content: payload.Content,
                attachmentCount: MediaPayloadBuilder.GetNormalizedAttachmentCount(payload.Attachment, payload.Attachments),
                requestedContentType: (int)payload.ContentType,
                out var ruleError))
            {
                await SendResponseAsync(context, message.SequenceId, new PrivateChatMessageResponse(false, MessageText: ruleError ?? "Message violates domain rules"));
                return;
            }

            var contentType = MediaPayloadBuilder.ResolveContentType(payload.ContentType, payload.Attachment, payload.Attachments);
            var normalizedContent = MediaPayloadBuilder.BuildMessageContent(payload.Content, payload.Attachment, payload.Attachments, payload.ParseMode);

            // Intercept messages sent to BotFather and execute command flow.
            if (await _botManagementService.IsBotFatherAsync(payload.ToUserId))
            {
                var botText = MediaPayloadBuilder.ExtractDisplayText(payload.Content, payload.ParseMode);
                var replies = await _botManagementService.ProcessBotFatherMessageAsync(session.UserId, botText);
                foreach (var reply in replies)
                {
                    var eventPayload = PayloadSerializer.Serialize(new PrivateChatMessageEventPayload(
                        Id: 0,
                        FromUserId: payload.ToUserId,
                        ToUserId: session.UserId,
                        Content: reply,
                        ContentType: MessageContentType.Text,
                        CreatedAt: DateTime.UtcNow,
                        DeliveredTo: [],
                        ReadBy: [],
                        FromUsername: "BotFather",
                        Username: "BotFather"));

                    await _messageSender.SendProtocolMessageAsync(
                        context.ConnectionId,
                        (ushort)MessageType.PrivateChatMessageEvent,
                        0,
                        eventPayload);
                }

                await SendResponseAsync(context, message.SequenceId,
                    new PrivateChatMessageResponse(true, 0, "BotFather command processed"));
                return;
            }

            // Check target user exists
            var targetUser = await _userSearchService.FindUserByIdAsync(payload.ToUserId);
            if (targetUser == null)
            {
                await SendResponseAsync(context, message.SequenceId, new PrivateChatMessageResponse(false, MessageText: "User not found"));
                return;
            }

            var privateMsg = await _messageService.SendPrivateMessageAsync(
                session.UserId, payload.ToUserId, normalizedContent, contentType);

            // Push the message to the recipient if they are online
            if (_sessionManager.TryGetConnectionIdByUserId(payload.ToUserId, out var recipientConnId))
            {
                var pushPayload = PayloadSerializer.Serialize(new PrivateChatMessageEventPayload(
                    Id: privateMsg.Id,
                    FromUserId: session.UserId,
                    ToUserId: payload.ToUserId,
                    Content: privateMsg.Content,
                    ContentType: privateMsg.ContentType,
                    CreatedAt: privateMsg.CreatedAt,
                    DeliveredTo: privateMsg.IsDelivered ? [payload.ToUserId] : [],
                    ReadBy: privateMsg.IsRead ? [payload.ToUserId] : [],
                    FromUsername: session.Username,
                    Username: session.Username));

                await _messageSender.SendProtocolMessageAsync(
                    recipientConnId,
                    (ushort)MessageType.PrivateChatMessageEvent,
                    0,
                    pushPayload);
            }

            await SendResponseAsync(context, message.SequenceId, 
                new PrivateChatMessageResponse(true, privateMsg.Id, "Message sent"));

            _logger.LogInformation("Private message sent from user {FromUserId} to user {ToUserId}", 
                session.UserId, payload.ToUserId);
        }
        catch (Exception ex)
        {
            _logger.LogHandlerError(ex, "private_message_send", context, message, _sessionManager);
            await SendResponseAsync(context, message.SequenceId, new PrivateChatMessageResponse(false, MessageText: "Internal server error"));
        }
    }

    private async Task SendResponseAsync(ConnectionContext context, ulong sequenceId, PrivateChatMessageResponse response)
    {
        await HandlerResponseSender.SendAsync(
            _messageSender,
            context,
            MessageType.PrivateChatMessage,
            sequenceId,
            response);
    }
}

internal static class MediaPayloadBuilder
{
    private const int MaxAttachmentsPerMessage = MediaPolicy.MaxAttachmentsPerMessage;
    private const long MaxSingleAttachmentBytes = MediaPolicy.MaxSingleAttachmentBytes;
    private const long MaxTotalAttachmentsBytes = MediaPolicy.MaxTotalAttachmentsBytes;

    public static MessageContentType ResolveContentType(
        MessageContentType requestedType,
        MediaAttachmentPayload? attachment,
        IReadOnlyList<MediaAttachmentPayload>? attachments = null)
    {
        var normalizedAttachments = NormalizeAttachments(attachment, attachments);
        if (normalizedAttachments.Count == 0)
        {
            return requestedType;
        }

        if (requestedType != MessageContentType.Text)
        {
            return requestedType;
        }

        if (normalizedAttachments.Count > 1)
        {
            var allImages = normalizedAttachments.All(a => a.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase));
            if (allImages)
            {
                return MessageContentType.Image;
            }

            var allVideo = normalizedAttachments.All(a => a.MimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase));
            if (allVideo)
            {
                return MessageContentType.Video;
            }

            var allAudio = normalizedAttachments.All(a => a.MimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase));
            if (allAudio)
            {
                return MessageContentType.Audio;
            }

            return MessageContentType.File;
        }

        var single = normalizedAttachments[0];
        if (single.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return MessageContentType.Image;
        }

        if (single.MimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
        {
            return MessageContentType.Video;
        }

        if (single.MimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
        {
            return MessageContentType.Audio;
        }

        return MessageContentType.File;
    }

    private static readonly HashSet<string> AllowedParseModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "markdown",
        "markdownv2",
        "html"
    };

    public static string BuildMessageContent(string? text, MediaAttachmentPayload? attachment, IReadOnlyList<MediaAttachmentPayload>? attachments, string? parseMode)
    {
        var normalizedText = BuildTextWithFormatting(text, parseMode);
        var normalizedAttachments = NormalizeAttachments(attachment, attachments);
        if (normalizedAttachments.Count == 0)
        {
            return normalizedText ?? string.Empty;
        }

        ValidateAttachments(normalizedAttachments);

        if (normalizedAttachments.Count == 1)
        {
            var single = normalizedAttachments[0];
            var storedSingle = new StoredMediaContent(
                Text: normalizedText,
                FileName: single.FileName,
                MimeType: single.MimeType,
                Base64Data: single.Base64Data,
                SizeBytes: single.SizeBytes);

            return JsonSerializer.Serialize(storedSingle);
        }

        var stored = new StoredMediaBatchContent(
            Kind: "media-batch",
            Text: normalizedText,
            Attachments: normalizedAttachments);

        return JsonSerializer.Serialize(stored);
    }

    public static int GetNormalizedAttachmentCount(MediaAttachmentPayload? attachment, IReadOnlyList<MediaAttachmentPayload>? attachments)
    {
        return NormalizeAttachments(attachment, attachments).Count;
    }

    public static string ExtractDisplayText(string? text, string? parseMode)
    {
        return BuildTextWithFormatting(text, parseMode) ?? string.Empty;
    }

    private static string? BuildTextWithFormatting(string? text, string? parseMode)
    {
        var normalizedText = text?.Trim();
        if (string.IsNullOrEmpty(normalizedText))
        {
            return normalizedText;
        }

        var normalizedMode = NormalizeParseMode(parseMode);
        if (normalizedMode == null)
        {
            return normalizedText;
        }

        var rich = new StoredRichTextContent(
            Kind: "rich-text",
            Text: normalizedText,
            ParseMode: normalizedMode);

        return JsonSerializer.Serialize(rich);
    }

    private static string? NormalizeParseMode(string? parseMode)
    {
        if (string.IsNullOrWhiteSpace(parseMode))
        {
            return null;
        }

        var normalized = parseMode.Trim().ToLowerInvariant();
        return AllowedParseModes.Contains(normalized) ? normalized : null;
    }

    private static IReadOnlyList<MediaAttachmentPayload> NormalizeAttachments(
        MediaAttachmentPayload? attachment,
        IReadOnlyList<MediaAttachmentPayload>? attachments)
    {
        if (attachment == null && (attachments == null || attachments.Count == 0))
        {
            return Array.Empty<MediaAttachmentPayload>();
        }

        if (attachment == null)
        {
            return attachments ?? Array.Empty<MediaAttachmentPayload>();
        }

        if (attachments == null || attachments.Count == 0)
        {
            return new[] { attachment };
        }

        var combined = new List<MediaAttachmentPayload>(attachments.Count + 1) { attachment };
        combined.AddRange(attachments);
        return combined;
    }

    private static void ValidateAttachments(IReadOnlyList<MediaAttachmentPayload> attachments)
    {
        if (attachments.Count == 0)
        {
            return;
        }

        if (attachments.Count > MaxAttachmentsPerMessage)
        {
            throw new ArgumentException($"Maximum {MaxAttachmentsPerMessage} attachments are allowed per message");
        }

        long totalBytes = 0;
        foreach (var attachment in attachments)
        {
            if (string.IsNullOrWhiteSpace(attachment.FileName))
            {
                throw new ArgumentException("Attachment file name is required");
            }

            if (string.IsNullOrWhiteSpace(attachment.MimeType))
            {
                throw new ArgumentException("Attachment MIME type is required");
            }

            if (string.IsNullOrWhiteSpace(attachment.Base64Data))
            {
                throw new ArgumentException("Attachment base64 payload is required");
            }

            var estimatedBytes = EstimateDecodedBytes(attachment.Base64Data);
            if (estimatedBytes <= 0)
            {
                throw new ArgumentException("Attachment payload is empty");
            }

            if (estimatedBytes > MaxSingleAttachmentBytes)
            {
                throw new ArgumentException($"Attachment '{attachment.FileName}' exceeds {MaxSingleAttachmentBytes / 1024}KB limit");
            }

            if (attachment.SizeBytes.HasValue && attachment.SizeBytes.Value != estimatedBytes)
            {
                throw new ArgumentException($"Attachment '{attachment.FileName}' size metadata mismatch");
            }

            totalBytes += estimatedBytes;
            if (totalBytes > MaxTotalAttachmentsBytes)
            {
                throw new ArgumentException($"Total attachments payload exceeds {MaxTotalAttachmentsBytes / 1024}KB limit");
            }
        }
    }

    private static int EstimateDecodedBytes(string base64Data)
    {
        var base64 = base64Data.Trim();
        if (base64.Length == 0)
        {
            return 0;
        }

        var padding = 0;
        if (base64.EndsWith("==", StringComparison.Ordinal))
        {
            padding = 2;
        }
        else if (base64.EndsWith("=", StringComparison.Ordinal))
        {
            padding = 1;
        }

        return Math.Max(0, (base64.Length * 3 / 4) - padding);
    }
}


// ===================== CHANNEL JOIN HANDLER =====================

public class ChannelJoinHandler : IMessageHandler
{
    public MessageType Type => MessageType.ChannelJoin;

    private readonly IChannelService _channelService;
    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;
    private readonly ILogger<ChannelJoinHandler> _logger;

    public ChannelJoinHandler(
        IChannelService channelService,
        SessionManager sessionManager,
        IMessageSender messageSender,
        ILogger<ChannelJoinHandler> logger)
    {
        _channelService = channelService;
        _sessionManager = sessionManager;
        _messageSender = messageSender;
        _logger = logger;
    }

    public async ValueTask HandleAsync(ConnectionContext context, Aegis.Protocol.Message message)
    {
        try
        {
            var session = _sessionManager.GetAuthenticatedSession(context.ConnectionId);
            if (session == null)
            {
                await SendResponseAsync(context, message.SequenceId,
                    new ChannelJoinResponse(false, Message: "Not authenticated"));
                return;
            }

            var payload = PayloadSerializer.Deserialize<ChannelJoinRequest>(message.Payload);
            if (payload == null || payload.ChannelId == 0)
            {
                await SendResponseAsync(context, message.SequenceId,
                    new ChannelJoinResponse(false, Message: "Invalid payload"));
                return;
            }

            (Channel channel, bool wasAlreadyMember) = await _channelService.JoinChannelAsync(
                session.UserId, payload.ChannelId);

            var summary = new ChannelSummary(
                channel.Id,
                channel.Name,
                channel.Description,
                (int)channel.Type,
                channel.MemberCount);

            await SendResponseAsync(context, message.SequenceId,
                new ChannelJoinResponse(true, summary,
                    wasAlreadyMember ? "Already a member" : "Joined channel"));

            _logger.LogInformation(
                "User {UserId} {Action} channel {ChannelId}",
                session.UserId,
                wasAlreadyMember ? "was already a member of" : "joined",
                payload.ChannelId);
        }
        catch (UnauthorizedAccessException ex)
        {
            await SendResponseAsync(context, message.SequenceId,
                new ChannelJoinResponse(false, Message: ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            await SendResponseAsync(context, message.SequenceId,
                new ChannelJoinResponse(false, Message: ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogHandlerError(ex, "channel_join", context, message, _sessionManager);
            await SendResponseAsync(context, message.SequenceId,
                new ChannelJoinResponse(false, Message: "Internal server error"));
        }
    }

    private async Task SendResponseAsync(ConnectionContext context, ulong sequenceId, ChannelJoinResponse response)
    {
        await HandlerResponseSender.SendAsync(
            _messageSender,
            context,
            MessageType.ChannelJoin,
            sequenceId,
            response);
    }
}
