using System.Text.Json;
using Aegis.Common;
using Aegis.Data.Repositories;
using Aegis.Protocol;
using Aegis.Transport;
using Microsoft.Extensions.Logging;

namespace Aegis.Handlers;

public record UserPresenceUpdateRequest(bool IsOnline, DateTime? ClientTimestamp = null);

public class UserPresenceHandler : IMessageHandler
{
    public MessageType Type => MessageType.UserPresence;

    private readonly SessionManager _sessionManager;
    private readonly IUserRepository _userRepository;
    private readonly ISessionRepository _sessionRepository;
    private readonly ILogger<UserPresenceHandler> _logger;

    public UserPresenceHandler(
        SessionManager sessionManager,
        IUserRepository userRepository,
        ISessionRepository sessionRepository,
        ILogger<UserPresenceHandler> logger)
    {
        _sessionManager = sessionManager;
        _userRepository = userRepository;
        _sessionRepository = sessionRepository;
        _logger = logger;
    }

    public async ValueTask HandleAsync(ConnectionContext context, Message message)
    {
        var session = _sessionManager.GetAuthenticatedSession(context.ConnectionId);
        if (session == null)
        {
            return;
        }

        UserPresenceUpdateRequest? request = null;
        try
        {
            request = JsonSerializer.Deserialize<UserPresenceUpdateRequest>(message.Payload);
        }
        catch (JsonException ex)
        {
            _logger.LogHandlerError(ex, "presence_invalid_json", context, message, _sessionManager);
            return;
        }

        if (request == null)
        {
            return;
        }

        _sessionManager.SetUserPresence(context.ConnectionId, request.IsOnline);

        if (!request.IsOnline)
        {
            var user = await _userRepository.GetByIdAsync(session.UserId);
            if (user != null)
            {
                user.LastSeenAt = DateTime.UtcNow;
                user.UpdatedAt = DateTime.UtcNow;
                await _userRepository.UpdateAsync(user);
            }

            var dbSession = await _sessionRepository.GetByConnectionIdAsync(context.ConnectionId.ToString());
            if (dbSession != null)
            {
                dbSession.LastActivityAt = DateTime.UtcNow;
                dbSession.IsActive = false;
                await _sessionRepository.UpdateAsync(dbSession);
            }
        }
    }
}
