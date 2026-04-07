using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Aegis.Common;
using Aegis.Data.Entities;
using Aegis.Data.Repositories;
using Aegis.Data.Services;
using Aegis.Protocol;
using Aegis.Transport;
using ProtocolMessage = Aegis.Protocol.Message;

namespace Aegis.Handlers;

/// <summary>
/// Handler for message read receipts
/// </summary>
public class MessageReadReceiptHandler : IMessageHandler
{
    public MessageType Type => MessageType.MessageReadReceipt;

    private readonly ILogger<MessageReadReceiptHandler> _logger;
    private readonly IServiceProvider _serviceProvider;

    public MessageReadReceiptHandler(
        ILogger<MessageReadReceiptHandler> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public async ValueTask HandleAsync(ConnectionContext context, ProtocolMessage message)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var deliveryService = scope.ServiceProvider.GetRequiredService<IMessageDeliveryService>();
            var messageRepository = scope.ServiceProvider.GetRequiredService<IMessageRepository>();
            var messageSender = scope.ServiceProvider.GetRequiredService<IMessageSender>();
            var sessionManager = scope.ServiceProvider.GetRequiredService<SessionManager>();

            // Parse read receipt payload
            var payload = PayloadSerializer.Deserialize<ReadReceiptPayload>(message.Payload);
            if (payload == null)
            {
                _logger.LogWarning("Invalid read receipt payload from connection {ConnectionId}", context.ConnectionId);
                return;
            }

            var session = sessionManager.GetAuthenticatedSession(context.ConnectionId);
            if (session == null)
            {
                _logger.LogWarning("Read receipt from unauthenticated connection {ConnectionId}", context.ConnectionId);
                return;
            }

            // Mark messages as read in both delivery-tracking table and message store
            foreach (var messageId in payload.MessageIds)
            {
                await deliveryService.MarkMessageAsReadAsync(messageId, session.UserId, null);
            }

            await messageRepository.MarkMessagesReadAsync(payload.MessageIds, session.UserId);

            // Forward read status to original senders if they are online
            var bySender = new Dictionary<ulong, List<ulong>>();
            foreach (var messageId in payload.MessageIds.Distinct())
            {
                var sourceMessage = await messageRepository.GetByIdAsync(messageId);
                if (sourceMessage == null || sourceMessage.FromUserId == 0)
                {
                    continue;
                }

                if (!bySender.TryGetValue(sourceMessage.FromUserId, out var ids))
                {
                    ids = new List<ulong>();
                    bySender[sourceMessage.FromUserId] = ids;
                }

                ids.Add(messageId);
            }

            foreach (var (senderUserId, ids) in bySender)
            {
                var senderConnectionIds = sessionManager.GetConnectionIdsByUserId(senderUserId);
                if (senderConnectionIds.Count == 0) continue;

                var eventPayload = PayloadSerializer.Serialize(new
                {
                    Success = true,
                    MessageIds = ids,
                    DeliveredTo = (ulong?)null,
                    ReadBy = session.UserId,
                    ProcessedAt = DateTime.UtcNow
                });

                foreach (var senderConnectionId in senderConnectionIds)
                {
                    await messageSender.SendProtocolMessageAsync(
                        senderConnectionId,
                        (ushort)MessageType.MessageStatusEvent,
                        0,
                        eventPayload);
                }
            }

            // Send confirmation back to sender
            await SendReadReceiptConfirmationAsync(context, messageSender, payload.MessageIds);

            // Cross-device read sync: push ReadSyncEvent to all other devices of the same user.
            var ownOtherConnections = sessionManager.GetConnectionIdsByUserId(session.UserId)
                .Where(c => c != context.ConnectionId)
                .ToList();

            if (ownOtherConnections.Count > 0)
            {
                var syncPayload = PayloadSerializer.Serialize(new
                {
                    MessageIds = payload.MessageIds,
                    ReadAt = DateTime.UtcNow
                });

                foreach (var otherConnId in ownOtherConnections)
                {
                    try
                    {
                        await messageSender.SendProtocolMessageAsync(
                            otherConnId,
                            (ushort)MessageType.ReadSyncEvent,
                            0,
                            syncPayload,
                            allowUnsigned: false);
                    }
                    catch { /* best-effort — the other device may be disconnecting */ }
                }
            }

            _logger.LogDebug("Processed read receipt for {Count} messages from user {UserId}",
                payload.MessageIds.Length, session.UserId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing read receipt from connection {ConnectionId}", context.ConnectionId);
        }
    }

    private async Task SendReadReceiptConfirmationAsync(
        ConnectionContext context,
        IMessageSender messageSender,
        ulong[] messageIds)
    {
        var responsePayload = PayloadSerializer.Serialize(new {
            Success = true,
            MessageIds = messageIds,
            ProcessedAt = DateTime.UtcNow
        });

        await messageSender.SendProtocolMessageAsync(
            context.ConnectionId,
            (ushort)MessageType.MessageReadReceiptResponse,
            0, // sequence ID not important for response
            responsePayload);
    }
}

/// <summary>
/// Handler for message delivery receipts
/// </summary>
public class MessageDeliveryReceiptHandler : IMessageHandler
{
    public MessageType Type => MessageType.MessageDeliveryReceipt;

    private readonly ILogger<MessageDeliveryReceiptHandler> _logger;
    private readonly IServiceProvider _serviceProvider;

    public MessageDeliveryReceiptHandler(
        ILogger<MessageDeliveryReceiptHandler> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public async ValueTask HandleAsync(ConnectionContext context, ProtocolMessage message)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var deliveryService = scope.ServiceProvider.GetRequiredService<IMessageDeliveryService>();
            var messageRepository = scope.ServiceProvider.GetRequiredService<IMessageRepository>();
            var messageSender = scope.ServiceProvider.GetRequiredService<IMessageSender>();
            var sessionManager = scope.ServiceProvider.GetRequiredService<SessionManager>();

            // Parse delivery receipt payload
            var payload = PayloadSerializer.Deserialize<DeliveryReceiptPayload>(message.Payload);
            if (payload == null)
            {
                _logger.LogWarning("Invalid delivery receipt payload from connection {ConnectionId}", context.ConnectionId);
                return;
            }

            var session = sessionManager.GetAuthenticatedSession(context.ConnectionId);
            if (session == null)
            {
                _logger.LogWarning("Delivery receipt from unauthenticated connection {ConnectionId}", context.ConnectionId);
                return;
            }

            // Mark messages as delivered
            foreach (var messageId in payload.MessageIds)
            {
                await deliveryService.MarkMessageAsDeliveredAsync(messageId, session.UserId, null);
            }

            await messageRepository.MarkMessagesDeliveredAsync(payload.MessageIds, session.UserId);

            var bySender = new Dictionary<ulong, List<ulong>>();
            foreach (var messageId in payload.MessageIds.Distinct())
            {
                var sourceMessage = await messageRepository.GetByIdAsync(messageId);
                if (sourceMessage == null || sourceMessage.FromUserId == 0)
                {
                    continue;
                }

                if (!bySender.TryGetValue(sourceMessage.FromUserId, out var ids))
                {
                    ids = new List<ulong>();
                    bySender[sourceMessage.FromUserId] = ids;
                }

                ids.Add(messageId);
            }

            foreach (var (senderUserId, ids) in bySender)
            {
                var senderConnectionIds = sessionManager.GetConnectionIdsByUserId(senderUserId);
                if (senderConnectionIds.Count == 0) continue;

                var eventPayload = PayloadSerializer.Serialize(new
                {
                    Success = true,
                    MessageIds = ids,
                    DeliveredTo = session.UserId,
                    ReadBy = (ulong?)null,
                    ProcessedAt = DateTime.UtcNow
                });

                foreach (var senderConnectionId in senderConnectionIds)
                {
                    await messageSender.SendProtocolMessageAsync(
                        senderConnectionId,
                        (ushort)MessageType.MessageStatusEvent,
                        0,
                        eventPayload);
                }
            }

            // Send confirmation back to sender
            await SendDeliveryReceiptConfirmationAsync(context, messageSender, payload.MessageIds);

            _logger.LogDebug("Processed delivery receipt for {Count} messages from user {UserId}",
                payload.MessageIds.Length, session.UserId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing delivery receipt from connection {ConnectionId}", context.ConnectionId);
        }
    }

    private async Task SendDeliveryReceiptConfirmationAsync(
        ConnectionContext context,
        IMessageSender messageSender,
        ulong[] messageIds)
    {
        var responsePayload = PayloadSerializer.Serialize(new {
            Success = true,
            MessageIds = messageIds,
            ProcessedAt = DateTime.UtcNow
        });

        await messageSender.SendProtocolMessageAsync(
            context.ConnectionId,
            (ushort)MessageType.MessageDeliveryReceiptResponse,
            0, // sequence ID not important for response
            responsePayload);
    }
}

/// <summary>
/// Payload model for read receipts
/// </summary>
internal record ReadReceiptPayload
{
    public ulong[] MessageIds { get; init; } = Array.Empty<ulong>();
    public DateTime ReadAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Payload model for delivery receipts
/// </summary>
internal record DeliveryReceiptPayload
{
    public ulong[] MessageIds { get; init; } = Array.Empty<ulong>();
    public DateTime DeliveredAt { get; init; } = DateTime.UtcNow;
    public string? DeviceId { get; init; }
}
