using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Aegis.Data.Repositories;

namespace Aegis.Server.Services
{
    /// <summary>
    /// Background service that periodically cleans up expired sessions from the database.
    /// Runs every hour to prevent unbounded table growth.
    /// </summary>
    public class SessionCleanupBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<SessionCleanupBackgroundService> _logger;

        // Cleanup interval: 1 hour
        private readonly TimeSpan _cleanupInterval = TimeSpan.FromHours(1);

        // Add random jitter (±5 minutes) to prevent thundering herd on multi-instance deployments
        private readonly Random _random = new(Environment.TickCount);
        private readonly TimeSpan _jitterWindow = TimeSpan.FromMinutes(5);

        public SessionCleanupBackgroundService(
            IServiceProvider serviceProvider,
            ILogger<SessionCleanupBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Session cleanup background service started");

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        // Calculate next cleanup time with jitter
                        var jittered = TimeSpan.FromMilliseconds(
                            _cleanupInterval.TotalMilliseconds +
                            (_random.NextDouble() - 0.5) * _jitterWindow.TotalMilliseconds * 2);

                        _logger.LogDebug("Next session cleanup in {Interval}", jittered);
                        await Task.Delay(jittered, stoppingToken);

                        if (stoppingToken.IsCancellationRequested)
                            break;

                        // Perform cleanup
                        await CleanupExpiredSessionsAsync(stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogInformation("Session cleanup background service is stopping");
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error during session cleanup");
                        // Continue on error - don't stop the service
                    }
                }
            }
            finally
            {
                _logger.LogInformation("Session cleanup background service stopped");
            }
        }

        private async Task CleanupExpiredSessionsAsync(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var sessionRepository = scope.ServiceProvider.GetRequiredService<ISessionRepository>();

            try
            {
                var now = DateTime.UtcNow;
                _logger.LogInformation("Starting cleanup of expired sessions (cutoff: {Cutoff})", now);

                // Batch delete expired sessions (max 1000 per transaction to avoid locking)
                int totalDeleted = 0;
                while (true)
                {
                    var expiredSessions = (await sessionRepository.GetExpiredSessionsAsync(
                        now, maxResults: 1000, cancellationToken)).ToList();

                    if (expiredSessions.Count == 0)
                        break;

                    // Delete this batch
                    foreach (var session in expiredSessions)
                    {
                        await sessionRepository.DeleteAsync(session.Id);
                    }

                    totalDeleted += expiredSessions.Count;
                    _logger.LogDebug("Deleted {Count} expired sessions (total so far: {Total})",
                        expiredSessions.Count, totalDeleted);
                }

                _logger.LogInformation("Session cleanup completed. Total sessions deleted: {Total}", totalDeleted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred during session cleanup");
                throw;
            }
        }
    }
}
