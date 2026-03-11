using Aegis.Protocol;
using Aegis.Transport;
using Aegis.Common.Logging;
using Aegis.Common;
using Aegis.Data.Services;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace Aegis.Handlers;

public class MessageHandler : IMessageHandler
{
    private readonly IAntiSpamClient _antiSpam;
    private readonly IMessageService _messageService;
    private readonly IMessageSender _messageSender;
    private readonly SessionManager _sessionManager;
    private readonly ILogger _logger;
    private bool _ackSent = false;
    private ulong _ackSequenceId = 0;
    private string _errorMessage = string.Empty;
    
    public Aegis.Protocol.MessageType Type => Aegis.Protocol.MessageType.Message;
    
    public MessageHandler(
        IAntiSpamClient antiSpam,
        IMessageService messageService,
        IMessageSender messageSender,
        SessionManager sessionManager,
        ILogger? logger = null)
    {
        _antiSpam = antiSpam;
        _messageService = messageService;
        _messageSender = messageSender;
        _sessionManager = sessionManager;
        _logger = logger ?? new Aegis.Transport.NullLogger();
    }
    
    public async ValueTask HandleAsync(ConnectionContext context, Message message)
    {
        var session = _sessionManager.GetAuthenticatedSession(context.ConnectionId);
        if (session == null)
        {
            await SendErrorAsync(context, message.SequenceId, "Not authenticated");
            return;
        }

        var allowed = await _antiSpam.CheckMessageAsync(context.ConnectionId, message.Payload);
        
        if (!allowed)
        {
            // отклонение сообщения
            await SendErrorAsync(context, message.SequenceId, "Message rejected by anti-spam");
            return;
        }
        
        var delivered = await RouteMessageToRecipient(context, message, session);
        if (!delivered)
        {
            await SendErrorAsync(context, message.SequenceId, "Message delivery failed");
            return;
        }

        await SendAckAsync(context, message.SequenceId);
    }
    
    private async Task<bool> RouteMessageToRecipient(ConnectionContext context, Message message, SessionInfo senderSession)
    {
        try
        {
            var request = ParseDirectMessageRequest(message.Payload);
            if (request == null || request.RecipientId == 0 || string.IsNullOrWhiteSpace(request.Content))
            {
                _logger.Warning($"Invalid direct message payload from connection {context.ConnectionId}");
                return false;
            }

            var normalizedContent = MediaPayloadBuilder.BuildMessageContent(
                request.Content,
                attachment: null,
                parseMode: request.ParseMode);

            var saved = await _messageService.SendPrivateMessageAsync(
                senderSession.UserId,
                request.RecipientId,
                normalizedContent,
                Aegis.Data.Entities.MessageContentType.Text);

            if (_sessionManager.TryGetConnectionIdByUserId(request.RecipientId, out var recipientConnectionId))
            {
                var pushPayload = JsonSerializer.SerializeToUtf8Bytes(new IncomingDirectMessage(
                    saved.Id,
                    senderSession.UserId,
                    senderSession.Username,
                    saved.Content,
                    saved.CreatedAt));

                await _messageSender.SendProtocolMessageAsync(
                    recipientConnectionId,
                    (ushort)Aegis.Protocol.MessageType.PrivateChatMessage,
                    message.SequenceId,
                    pushPayload);

                _logger.Info($"Message {saved.Id} delivered from user {senderSession.UserId} to user {request.RecipientId}");
            }
            else
            {
                _logger.Info($"Recipient {request.RecipientId} is offline; message stored for deferred delivery");
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"Error routing message from connection {context.ConnectionId}", ex);
            return false;
        }
    }

    private static DirectMessageRequest? ParseDirectMessageRequest(byte[] payload)
    {
        if (payload.Length == 0)
        {
            return null;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<DirectMessageRequest>(payload, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (parsed != null)
            {
                return parsed;
            }
        }
        catch
        {
            // Fall back to legacy binary format.
        }

        if (payload.Length < 20)
        {
            return null;
        }

        var recipientId = BinaryPrimitives.ReadUInt64BigEndian(payload.AsSpan(8, 8));
        var content = Encoding.UTF8.GetString(payload.AsSpan(20));

        return new DirectMessageRequest
        {
            RecipientId = recipientId,
            Content = content
        };
    }
    
    public bool AckSent => _ackSent;
    public ulong AckSequenceId => _ackSequenceId;
    public string ErrorMessage => _errorMessage;
    
    private async Task SendAckAsync(ConnectionContext context, ulong sequenceId)
    {
        try
        {
            var ackMessage = new Message
            {
                Magic = ProtocolConstants.Magic,
                VersionMajor = ProtocolConstants.VersionMajor,
                VersionMinor = ProtocolConstants.VersionMinor,
                Type = Aegis.Protocol.MessageType.Ack,
                SequenceId = sequenceId,
                PayloadLength = 0,
                Payload = Array.Empty<byte>()
            };

            await _messageSender.SendProtocolMessageAsync(
                context.ConnectionId,
                (ushort)Aegis.Protocol.MessageType.Ack,
                sequenceId,
                Array.Empty<byte>());
            
            _ackSent = true;
            _ackSequenceId = sequenceId;
            
            _logger.Debug($"ACK sent for sequence {sequenceId} to connection {context.ConnectionId}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Error sending ACK to connection {context.ConnectionId}", ex);
            throw;
        }
    }
    
    private async Task SendErrorAsync(ConnectionContext context, ulong sequenceId, string error)
    {
        try
        {
            var errorMessage = new Message
            {
                Magic = ProtocolConstants.Magic,
                VersionMajor = ProtocolConstants.VersionMajor,
                VersionMinor = ProtocolConstants.VersionMinor,
                Type = Aegis.Protocol.MessageType.Error,
                SequenceId = sequenceId,
                PayloadLength = (uint)System.Text.Encoding.UTF8.GetByteCount(error),
                Payload = System.Text.Encoding.UTF8.GetBytes(error)
            };

            await _messageSender.SendProtocolMessageAsync(
                context.ConnectionId,
                (ushort)Aegis.Protocol.MessageType.Error,
                sequenceId,
                System.Text.Encoding.UTF8.GetBytes(error));
            
            _errorMessage = error;
            
            _logger.Warning($"Error sent to connection {context.ConnectionId}: {error}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Error sending error to connection {context.ConnectionId}", ex);
            throw;
        }
    }

    private sealed class DirectMessageRequest
    {
        public ulong RecipientId { get; set; }
        public string Content { get; set; } = string.Empty;
        public string? ParseMode { get; set; }
    }

    private sealed record IncomingDirectMessage(
        ulong MessageId,
        ulong FromUserId,
        string FromUsername,
        string Content,
        DateTime CreatedAtUtc);
}
