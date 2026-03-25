using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Aegis.Data.Entities;
using Aegis.Data.Repositories;
using Aegis.Protocol;

namespace Aegis.Data.Services;

/// <summary>
/// Service for managing message delivery status and read receipts
/// </summary>
public interface IMessageDeliveryService
{
    Task<MessageDelivery> CreateDeliveryRecordAsync(ulong messageId, ulong userId, DeliveryStatus status = DeliveryStatus.Sent, string? deviceId = null);
    Task MarkMessageAsDeliveredAsync(ulong messageId, ulong userId, string? deviceId = null);
    Task MarkMessageAsReadAsync(ulong messageId, ulong userId, string? deviceId = null);
    Task<IEnumerable<MessageDelivery>> GetDeliveryStatusAsync(ulong messageId);
    Task<IEnumerable<MessageDelivery>> GetUndeliveredMessagesAsync(ulong userId);
    Task<IEnumerable<MessageDelivery>> GetUnreadMessagesAsync(ulong userId);
    Task<Dictionary<ulong, DeliveryStatus>> GetMessageDeliveryStatusesAsync(ulong userId, IEnumerable<ulong> messageIds);
}

public class MessageDeliveryService : IMessageDeliveryService
{
    private readonly IMessageDeliveryRepository _deliveryRepository;
    private readonly ILogger<MessageDeliveryService> _logger;

    public MessageDeliveryService(
        IMessageDeliveryRepository deliveryRepository,
        ILogger<MessageDeliveryService> logger)
    {
        _deliveryRepository = deliveryRepository;
        _logger = logger;
    }

    public async Task<MessageDelivery> CreateDeliveryRecordAsync(ulong messageId, ulong userId, DeliveryStatus status = DeliveryStatus.Sent, string? deviceId = null)
    {
        var delivery = new MessageDelivery
        {
            MessageId = messageId,
            UserId = userId,
            Status = status,
            DeviceId = deviceId,
            StatusUpdatedAt = DateTime.UtcNow
        };

        return await _deliveryRepository.CreateAsync(delivery);
    }

    public async Task MarkMessageAsDeliveredAsync(ulong messageId, ulong userId, string? deviceId = null)
    {
        try
        {
            await _deliveryRepository.MarkMessageAsDeliveredAsync(messageId, userId, deviceId);
            _logger.LogDebug("Message {MessageId} marked as delivered for user {UserId}", messageId, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark message {MessageId} as delivered for user {UserId}", messageId, userId);
            throw;
        }
    }

    public async Task MarkMessageAsReadAsync(ulong messageId, ulong userId, string? deviceId = null)
    {
        try
        {
            await _deliveryRepository.MarkMessageAsReadAsync(messageId, userId, deviceId);
            _logger.LogDebug("Message {MessageId} marked as read for user {UserId}", messageId, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark message {MessageId} as read for user {UserId}", messageId, userId);
            throw;
        }
    }

    public async Task<IEnumerable<MessageDelivery>> GetDeliveryStatusAsync(ulong messageId)
    {
        return await _deliveryRepository.GetByMessageIdAsync(messageId);
    }

    public async Task<IEnumerable<MessageDelivery>> GetUndeliveredMessagesAsync(ulong userId)
    {
        return await _deliveryRepository.GetUndeliveredMessagesAsync(userId);
    }

    public async Task<IEnumerable<MessageDelivery>> GetUnreadMessagesAsync(ulong userId)
    {
        return await _deliveryRepository.GetUnreadMessagesAsync(userId);
    }

    public async Task<Dictionary<ulong, DeliveryStatus>> GetMessageDeliveryStatusesAsync(ulong userId, IEnumerable<ulong> messageIds)
    {
        var result = new Dictionary<ulong, DeliveryStatus>();
        var messageIdsList = messageIds.ToList();

        foreach (var messageId in messageIdsList)
        {
            var delivery = await _deliveryRepository.GetDeliveryStatusAsync(messageId, userId);
            result[messageId] = delivery?.Status ?? DeliveryStatus.Sent;
        }

        return result;
    }
}
