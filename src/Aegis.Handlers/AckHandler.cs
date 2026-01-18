using System.Buffers.Binary;
using Aegis.Protocol;
using Aegis.Transport;
using Aegis.Common;
using Aegis.Common.Logging;

namespace Aegis.Handlers;

/// <summary>
/// Handles acknowledgment messages (Ack)
/// </summary>
public class AckHandler : IMessageHandler
{
    private readonly AcknowledgmentManager _ackManager;
    private readonly ILogger _logger;

    public MessageType Type => MessageType.Ack;

    public AckHandler(AcknowledgmentManager ackManager, ILogger? logger = null)
    {
        _ackManager = ackManager;
        _logger = logger ?? new Aegis.Transport.NullLogger();
    }

    public async ValueTask HandleAsync(ConnectionContext context, Message message)
    {
        try
        {
            if (message.PayloadLength < 9) // 8 bytes for sequence + 1 byte for status
            {
                _logger.Warning("Invalid Ack message payload size");
                return;
            }

            // Parse Ack payload
            var sequenceId = BinaryPrimitives.ReadUInt64BigEndian(message.Payload.AsSpan(0, 8));
            var status = (AckStatus)message.Payload[8];

            _logger.Info($"Received Ack for message {sequenceId} with status {status}");

            if (status == AckStatus.Ok)
            {
                _ackManager.AcknowledgeMessage(sequenceId);
            }
            else if (status == AckStatus.Retry)
            {
                // Retransmit the message
                _ackManager.IncrementRetryCount(sequenceId);
                _logger.Info($"Retransmission requested for message {sequenceId}");
            }
            else
            {
                _logger.Warning($"Ack received with error status: {status}");
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.Error("Error handling Ack message", ex);
            throw;
        }
    }
}

/// <summary>
/// Handles not-acknowledge messages (Nack)
/// </summary>
public class NackHandler : IMessageHandler
{
    private readonly AcknowledgmentManager _ackManager;
    private readonly IMessageSender _messageSender;
    private readonly ILogger _logger;

    public MessageType Type => MessageType.Nack;

    public NackHandler(AcknowledgmentManager ackManager, IMessageSender messageSender, ILogger? logger = null)
    {
        _ackManager = ackManager;
        _messageSender = messageSender;
        _logger = logger ?? new Aegis.Transport.NullLogger();
    }

    public async ValueTask HandleAsync(ConnectionContext context, Message message)
    {
        try
        {
            if (message.PayloadLength < 8)
            {
                _logger.Warning("Invalid Nack message payload size");
                return;
            }

            var sequenceId = BinaryPrimitives.ReadUInt64BigEndian(message.Payload.AsSpan(0, 8));
            _logger.Info($"Received Nack for message {sequenceId}, preparing retransmission");

            // Try to retransmit the message
            if (_ackManager.ShouldRetransmit(sequenceId, out var pending) && pending != null)
            {
                _ackManager.IncrementRetryCount(sequenceId);
                
                _logger.Info($"Retransmitting message {sequenceId}");
                await _messageSender.SendMessageAsync(context.ConnectionId, pending.MessageData);
            }
            else
            {
                _logger.Warning($"Cannot retransmit message {sequenceId} - max retries exceeded or not found");
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.Error("Error handling Nack message", ex);
            throw;
        }
    }
}

/// <summary>
/// Handles retransmit request messages
/// </summary>
public class RetransmitRequestHandler : IMessageHandler
{
    private readonly AcknowledgmentManager _ackManager;
    private readonly IMessageSender _messageSender;
    private readonly ILogger _logger;

    public MessageType Type => MessageType.RetransmitRequest;

    public RetransmitRequestHandler(AcknowledgmentManager ackManager, IMessageSender messageSender, ILogger? logger = null)
    {
        _ackManager = ackManager;
        _messageSender = messageSender;
        _logger = logger ?? new Aegis.Transport.NullLogger();
    }

    public async ValueTask HandleAsync(ConnectionContext context, Message message)
    {
        try
        {
            if (message.PayloadLength < 8)
            {
                _logger.Warning("Invalid RetransmitRequest payload size");
                return;
            }

            var firstSequenceId = BinaryPrimitives.ReadUInt64BigEndian(message.Payload.AsSpan(0, 8));
            
            _logger.Info($"Received retransmit request starting from sequence {firstSequenceId}");

            var pending = _ackManager.GetPendingMessages(context.ConnectionId);
            var toRetransmit = pending
                .Where(m => m.SequenceId >= firstSequenceId)
                .OrderBy(m => m.SequenceId)
                .ToList();

            foreach (var msg in toRetransmit)
            {
                _logger.Info($"Retransmitting message {msg.SequenceId}");
                await _messageSender.SendMessageAsync(context.ConnectionId, msg.MessageData);
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.Error("Error handling RetransmitRequest message", ex);
            throw;
        }
    }
}
