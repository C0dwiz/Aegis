using Aegis.Protocol;
using Aegis.Transport;
using Aegis.Common.Logging;
using Aegis.Common;
using Aegis.Data.Services;
using Aegis.Data.Repositories;
using Aegis.Crypto;
using System.Security.Cryptography;
using System.Buffers.Binary;
using System.Text;

namespace Aegis.Handlers;

public class MessageHandler : IMessageHandler
{
    private readonly IAntiSpamClient _antiSpam;
    private readonly IMessageService _messageService;
    private readonly IMessageSender _messageSender;
    private readonly SessionManager _sessionManager;
    private readonly ISignalChainStateRepository? _signalChainStateRepository;
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
        ILogger? logger = null,
        ISignalChainStateRepository? signalChainStateRepository = null)
    {
        _antiSpam = antiSpam;
        _messageService = messageService;
        _messageSender = messageSender;
        _sessionManager = sessionManager;
        _signalChainStateRepository = signalChainStateRepository;
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

            var signalInfo = await AdvanceSignalChainAsync(senderSession.UserId, request.RecipientId);

            var normalizedContent = MediaPayloadBuilder.BuildMessageContent(
                request.Content,
                attachment: null,
                attachments: null,
                parseMode: request.ParseMode);

            var saved = await _messageService.SendPrivateMessageAsync(
                senderSession.UserId,
                request.RecipientId,
                normalizedContent,
                Aegis.Data.Entities.MessageContentType.Text);

            if (_sessionManager.TryGetConnectionIdByUserId(request.RecipientId, out var recipientConnectionId))
            {
                var pushPayload = PayloadSerializer.Serialize(new IncomingDirectMessage(
                    saved.Id,
                    senderSession.UserId,
                    senderSession.Username,
                    saved.Content,
                    saved.CreatedAt,
                    signalInfo));

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
            var parsed = PayloadSerializer.Deserialize<DirectMessageRequest>(payload);

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
        public SignalV3Envelope? SignalV3 { get; set; }
    }

    private async Task<SignalV3DeliveryInfo?> AdvanceSignalChainAsync(ulong senderUserId, ulong recipientUserId)
    {
        if (_signalChainStateRepository == null)
        {
            return null;
        }

        var state = await _signalChainStateRepository.GetOrCreateAsync(senderUserId, recipientUserId);

        var ratchet = new DoubleRatchetAlgorithm();
        ratchet.Initialize(
            Convert.FromBase64String(state.RootKeyBase64),
            Convert.FromBase64String(state.SendingChainKeyBase64),
            Convert.FromBase64String(state.ReceivingChainKeyBase64),
            state.NextSendingMessageNumber,
            state.NextReceivingMessageNumber);

        var (messageNumber, messageKey) = ratchet.NextSendingMessageKey();
        var snapshot = ratchet.ExportState();

        state.RootKeyBase64 = Convert.ToBase64String(snapshot.RootKey);
        state.SendingChainKeyBase64 = Convert.ToBase64String(snapshot.SendingChainKey);
        state.ReceivingChainKeyBase64 = Convert.ToBase64String(snapshot.ReceivingChainKey);
        state.NextSendingMessageNumber = snapshot.NextSendingMessageNumber;
        state.NextReceivingMessageNumber = snapshot.NextReceivingMessageNumber;
        state.LastMessageKeyHash = Convert.ToHexString(SHA256.HashData(messageKey));

        await _signalChainStateRepository.UpdateAsync(state);

        return new SignalV3DeliveryInfo(
            messageNumber,
            state.LastMessageKeyHash[..16],
            state.UpdatedAt);
    }

    private sealed record SignalV3Envelope(string? CiphertextBase64, uint? MessageNumber = null);

    private sealed record SignalV3DeliveryInfo(
        uint MessageNumber,
        string MessageKeyId,
        DateTime RatchetUpdatedAtUtc);

    private sealed record IncomingDirectMessage(
        ulong MessageId,
        ulong FromUserId,
        string FromUsername,
        string Content,
        DateTime CreatedAtUtc,
        SignalV3DeliveryInfo? SignalV3 = null);
}
