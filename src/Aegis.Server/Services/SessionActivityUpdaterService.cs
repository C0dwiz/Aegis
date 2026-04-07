using Aegis.Common;
using Aegis.Data.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aegis.Server.Services;

/// <summary>
/// Background service that periodically flushes <c>LastActivityAt</c> to the database
/// for all currently-connected sessions. This ensures that the eviction logic always
/// boots the genuinely least-recently-used device rather than one whose DB timestamp
/// is stale due to being long-lived.
/// Runs every 5 minutes.
/// </summary>
public sealed class SessionActivityUpdaterService : BackgroundService
{
    private readonly SessionManager _sessionManager;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SessionActivityUpdaterService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    public SessionActivityUpdaterService(
        SessionManager sessionManager,
        IServiceProvider serviceProvider,
        ILogger<SessionActivityUpdaterService> logger)
    {
        _sessionManager = sessionManager;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await FlushActivityAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Session activity flush failed");
            }
        }
    }

    private async Task FlushActivityAsync(CancellationToken cancellationToken)
    {
        // Collect DB session IDs for all authenticated in-memory connections.
        // We match by the ConnectionId stored on the DB Session entity.
        var onlineUserIds = _sessionManager.GetOnlineUserIds();
        if (onlineUserIds.Count == 0) return;

        using var scope = _serviceProvider.CreateScope();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<ISessionRepository>();

        var dbSessionIds = new List<ulong>();
        foreach (var userId in onlineUserIds)
        {
            var connIds = _sessionManager.GetConnectionIdsByUserId(userId);
            foreach (var connId in connIds)
            {
                var s = _sessionManager.GetSession(connId);
                if (s == null) continue;

                // The DB Session.ConnectionId is the string representation of the TCP connectionId.
                var dbSessions = await sessionRepo.GetUserActiveSessions(userId);
                foreach (var dbSession in dbSessions)
                {
                    if (dbSession.ConnectionId == connId.ToString())
                        dbSessionIds.Add(dbSession.Id);
                }
            }
        }

        if (dbSessionIds.Count == 0) return;

        await sessionRepo.UpdateLastActivityAsync(dbSessionIds);
        _logger.LogDebug("Flushed LastActivityAt for {Count} active DB sessions", dbSessionIds.Count);
    }
}
