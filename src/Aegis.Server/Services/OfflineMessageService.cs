using Aegis.Common;
using Aegis.Common.Configuration;
using Aegis.Data.Repositories;
using Aegis.Handlers;
using Aegis.Protocol;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Server.Services;

public sealed class OfflineMessageService : BackgroundService
{
    private readonly SessionManager _sessionManager;
    private readonly IMessageRepository _messageRepository;
    private readonly IMessageSender _messageSender;
    private readonly ILogger<OfflineMessageService> _logger;
    private readonly OfflineMessageOptions _options;

    public OfflineMessageService(
        SessionManager sessionManager,
        IMessageRepository messageRepository,
        IMessageSender messageSender,
        ILogger<OfflineMessageService> logger,
        IOptions<OfflineMessageOptions> options)
    {
        _sessionManager = sessionManager;
        _messageRepository = messageRepository;
        _messageSender = messageSender;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.DeliveryIntervalSeconds));
        using var timer = new PeriodicTimer(interval);
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
        var ttl = _options.MessageTtlSeconds > 0
            ? TimeSpan.FromSeconds(_options.MessageTtlSeconds)
            : TimeSpan.MaxValue;
        var cutoff = _options.MessageTtlSeconds > 0
            ? DateTime.UtcNow - ttl
            : DateTime.MinValue;

        var onlineUserIds = _sessionManager.GetOnlineUserIds();
        if (onlineUserIds.Count == 0) return;

        // Process each online user in parallel (bounded by the number of online users,
        // not unbounded Task.WhenAll — we add degree-of-parallelism via Parallel.ForEachAsync).
        await Parallel.ForEachAsync(
            onlineUserIds,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Min(onlineUserIds.Count, 32), CancellationToken = cancellationToken },
            async (userId, ct) =>
            {
                ct.ThrowIfCancellationRequested();

                var connectionIds = _sessionManager.GetConnectionIdsByUserId(userId);
                if (connectionIds.Count == 0) return;

                await _messageRepository.TrimUndeliveredMessagesAsync(userId, _options.MaxQueuedPerUser);
                var pending = (await _messageRepository.GetUndeliveredMessagesAsync(userId))
                    .OrderBy(m => m.CreatedAt)
                    .Where(m => m.CreatedAt >= cutoff)   // skip messages older than TTL
                    .Take(_options.MaxQueuedPerUser)
                    .ToList();

                if (pending.Count == 0) return;

                var deliveredIds = new List<ulong>(pending.Count);
                foreach (var message in pending)
                {
                    // Serialize once; fan out to all active devices of this user.
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

                    // Deliver to all active devices in parallel.
                    var sendTasks = connectionIds.Select(async connectionId =>
                    {
                        try
                        {
                            await _messageSender.SendProtocolMessageAsync(
                                connectionId,
                                (ushort)MessageType.PrivateChatMessageEvent,
                                0,
                                payload,
                                allowUnsigned: false);
                            return true;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to deliver offline message {MessageId} to connection {ConnectionId}", message.Id, connectionId);
                            return false;
                        }
                    });

                    var results = await Task.WhenAll(sendTasks);
                    if (results.Any(r => r)) deliveredIds.Add(message.Id);
                }

                if (deliveredIds.Count > 0)
                    await _messageRepository.MarkMessagesDeliveredAsync(deliveredIds, userId);
            });
    }
}
