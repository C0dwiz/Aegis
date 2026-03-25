using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Aegis.Data.Entities;

namespace Aegis.Data.Repositories;

/// <summary>
/// Repository for managing message delivery status
/// </summary>
public interface IMessageDeliveryRepository
{
    Task<MessageDelivery?> GetAsync(ulong id);
    Task<IEnumerable<MessageDelivery>> GetByMessageIdAsync(ulong messageId);
    Task<IEnumerable<MessageDelivery>> GetByUserIdAsync(ulong userId);
    Task<MessageDelivery> CreateAsync(MessageDelivery delivery);
    Task<MessageDelivery> UpdateAsync(MessageDelivery delivery);
    Task<bool> DeleteAsync(ulong id);
    Task<MessageDelivery?> GetDeliveryStatusAsync(ulong messageId, ulong userId);
    Task<IEnumerable<MessageDelivery>> GetUndeliveredMessagesAsync(ulong userId);
    Task<IEnumerable<MessageDelivery>> GetUnreadMessagesAsync(ulong userId);
    Task MarkMessageAsDeliveredAsync(ulong messageId, ulong userId, string? deviceId = null);
    Task MarkMessageAsReadAsync(ulong messageId, ulong userId, string? deviceId = null);
}

public class MessageDeliveryRepository : IMessageDeliveryRepository
{
    private readonly AegisDbContext _context;

    public MessageDeliveryRepository(AegisDbContext context)
    {
        _context = context;
    }

    public async Task<MessageDelivery?> GetAsync(ulong id)
    {
        return await _context.MessageDeliveries.FindAsync(id);
    }

    public async Task<IEnumerable<MessageDelivery>> GetByMessageIdAsync(ulong messageId)
    {
        return await _context.MessageDeliveries
            .Where(md => md.MessageId == messageId)
            .ToListAsync();
    }

    public async Task<IEnumerable<MessageDelivery>> GetByUserIdAsync(ulong userId)
    {
        return await _context.MessageDeliveries
            .Where(md => md.UserId == userId)
            .OrderByDescending(md => md.StatusUpdatedAt)
            .ToListAsync();
    }

    public async Task<MessageDelivery> CreateAsync(MessageDelivery delivery)
    {
        _context.MessageDeliveries.Add(delivery);
        await _context.SaveChangesAsync();
        return delivery;
    }

    public async Task<MessageDelivery> UpdateAsync(MessageDelivery delivery)
    {
        _context.MessageDeliveries.Update(delivery);
        await _context.SaveChangesAsync();
        return delivery;
    }

    public async Task<bool> DeleteAsync(ulong id)
    {
        var delivery = await GetAsync(id);
        if (delivery == null)
            return false;

        _context.MessageDeliveries.Remove(delivery);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<MessageDelivery?> GetDeliveryStatusAsync(ulong messageId, ulong userId)
    {
        return await _context.MessageDeliveries
            .FirstOrDefaultAsync(md => md.MessageId == messageId && md.UserId == userId);
    }

    public async Task<IEnumerable<MessageDelivery>> GetUndeliveredMessagesAsync(ulong userId)
    {
        return await _context.MessageDeliveries
            .Where(md => md.UserId == userId && md.Status == DeliveryStatus.Sent)
            .ToListAsync();
    }

    public async Task<IEnumerable<MessageDelivery>> GetUnreadMessagesAsync(ulong userId)
    {
        return await _context.MessageDeliveries
            .Where(md => md.UserId == userId && md.Status < DeliveryStatus.Read)
            .ToListAsync();
    }

    public async Task MarkMessageAsDeliveredAsync(ulong messageId, ulong userId, string? deviceId = null)
    {
        var delivery = await GetDeliveryStatusAsync(messageId, userId);
        if (delivery != null && delivery.Status < DeliveryStatus.Delivered)
        {
            delivery.Status = DeliveryStatus.Delivered;
            delivery.StatusUpdatedAt = DateTime.UtcNow;
            delivery.DeviceId = deviceId;
            await UpdateAsync(delivery);
        }
    }

    public async Task MarkMessageAsReadAsync(ulong messageId, ulong userId, string? deviceId = null)
    {
        var delivery = await GetDeliveryStatusAsync(messageId, userId);
        if (delivery != null && delivery.Status < DeliveryStatus.Read)
        {
            delivery.Status = DeliveryStatus.Read;
            delivery.StatusUpdatedAt = DateTime.UtcNow;
            delivery.DeviceId = deviceId;
            await UpdateAsync(delivery);
        }
    }
}
