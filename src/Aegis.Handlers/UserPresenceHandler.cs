using Aegis.Common;
using Aegis.Data.Repositories;
using Aegis.Protocol;
using Aegis.Transport;
using MessagePack;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json;

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
            request = PayloadSerializer.Deserialize<UserPresenceUpdateRequest>(message.Payload);
        }
        catch (MessagePackSerializationException ex)
        {
            if (!TryDeserializeCompatibilityPayload(message.Payload, out request))
            {
                _logger.LogHandlerError(ex, "presence_invalid_json", context, message, _sessionManager);
                return;
            }
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

    private static bool TryDeserializeCompatibilityPayload(byte[] payload, out UserPresenceUpdateRequest? request)
    {
        request = null;

        try
        {
            using var document = JsonDocument.Parse(MessagePackSerializer.ConvertToJson(payload));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!document.RootElement.TryGetProperty("IsOnline", out var isOnlineElement)
                || isOnlineElement.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            {
                return false;
            }

            DateTime? clientTimestamp = null;
            if (document.RootElement.TryGetProperty("ClientTimestamp", out var timestampElement)
                && timestampElement.ValueKind != JsonValueKind.Null)
            {
                clientTimestamp = ParseClientTimestamp(timestampElement);
            }

            request = new UserPresenceUpdateRequest(isOnlineElement.GetBoolean(), clientTimestamp);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static DateTime? ParseClientTimestamp(JsonElement timestampElement)
    {
        if (timestampElement.ValueKind == JsonValueKind.String)
        {
            var raw = timestampElement.GetString();
            if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedOffset))
            {
                return parsedOffset.UtcDateTime;
            }

            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedDateTime))
            {
                return parsedDateTime.Kind == DateTimeKind.Utc
                    ? parsedDateTime
                    : parsedDateTime.ToUniversalTime();
            }

            return null;
        }

        return timestampElement.ValueKind switch
        {
            JsonValueKind.Number when timestampElement.TryGetInt64(out var unixMilliseconds) =>
                DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds).UtcDateTime,
            _ => null
        };
    }
}
