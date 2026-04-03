using Aegis.Common;
using Aegis.Data.Repositories;
using Aegis.Handlers;
using Aegis.Protocol;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aegis.Server.Services;

public sealed class OfflineMessageService : BackgroundService
{
    private const int MaxQueuedUndeliveredPerUser = 1000;
    private readonly SessionManager _sessionManager;
    private readonly IMessageRepository _messageRepository;
    private readonly IMessageSender _messageSender;
    private readonly ILogger<OfflineMessageService> _logger;

    public OfflineMessageService(
        SessionManager sessionManager,
        IMessageRepository messageRepository,
        IMessageSender messageSender,
        ILogger<OfflineMessageService> logger)
    {
        _sessionManager = sessionManager;
        _messageRepository = messageRepository;
        _messageSender = messageSender;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessOnlineUsersAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Offline message loop iteration failed");
            }
        }
    }

    private async Task ProcessOnlineUsersAsync(CancellationToken cancellationToken)
    {
        foreach (var userId in _sessionManager.GetOnlineUserIds())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_sessionManager.TryGetConnectionIdByUserId(userId, out var connectionId))
            {
                continue;
            }

            await _messageRepository.TrimUndeliveredMessagesAsync(userId, MaxQueuedUndeliveredPerUser);
            var pending = (await _messageRepository.GetUndeliveredMessagesAsync(userId))
                .OrderBy(m => m.CreatedAt)
                .Take(MaxQueuedUndeliveredPerUser)
                .ToList();

            if (pending.Count == 0)
            {
                continue;
            }

            var deliveredIds = new List<ulong>(pending.Count);
            foreach (var message in pending)
            {
                var payload = PayloadSerializer.Serialize(new PrivateChatMessageEventPayload(
                    message.Id,
                    message.FromUserId,
                    message.ToUserId,
                    message.Content,
                    message.ContentType,
                    message.CreatedAt,
                    message.IsDelivered ? new[] { message.ToUserId } : Array.Empty<ulong>(),
                    message.IsRead ? new[] { message.ToUserId } : Array.Empty<ulong>(),
                    message.FromUser?.Username,
                    message.ToUser?.Username));

                await _messageSender.SendProtocolMessageAsync(
                    connectionId,
                    (ushort)MessageType.PrivateChatMessageEvent,
                    0,
                    payload,
                    allowUnsigned: false);

                deliveredIds.Add(message.Id);
            }

            if (deliveredIds.Count > 0)
            {
                await _messageRepository.MarkMessagesDeliveredAsync(deliveredIds, userId);
            }
        }
    }
}
