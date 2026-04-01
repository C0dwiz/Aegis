using Aegis.Common;
using Aegis.Data.Repositories;
using Aegis.Protocol;
using Aegis.Transport;
using MessagePack;
using MessagePack.Resolvers;
using Microsoft.Extensions.Logging;
using System.Globalization;

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
            var dictionary = MessagePackSerializer.Deserialize<Dictionary<string, object?>>(payload,
                MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance));

            if (dictionary.Count == 0 || !dictionary.TryGetValue("IsOnline", out var isOnlineValue))
            {
                return false;
            }

            if (!TryGetBoolean(isOnlineValue, out var isOnline))
            {
                return false;
            }

            DateTime? clientTimestamp = null;
            if (dictionary.TryGetValue("ClientTimestamp", out var timestampValue) && timestampValue != null)
            {
                clientTimestamp = ParseClientTimestamp(timestampValue);
            }

            request = new UserPresenceUpdateRequest(isOnline, clientTimestamp);
            return true;
        }
        catch (Exception)
        {
            return TryDeserializeJsonPayload(payload, out request);
        }
    }

    private static bool TryDeserializeJsonPayload(byte[] payload, out UserPresenceUpdateRequest? request)
    {
        request = null;

        try
        {
            var json = System.Text.Encoding.UTF8.GetString(payload);
            using var document = System.Text.Json.JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                return false;
            }

            if (!document.RootElement.TryGetProperty("IsOnline", out var isOnlineElement)
                || isOnlineElement.ValueKind is not System.Text.Json.JsonValueKind.True and not System.Text.Json.JsonValueKind.False)
            {
                return false;
            }

            DateTime? clientTimestamp = null;
            if (document.RootElement.TryGetProperty("ClientTimestamp", out var timestampElement)
                && timestampElement.ValueKind != System.Text.Json.JsonValueKind.Null)
            {
                clientTimestamp = ParseClientTimestamp(timestampElement.GetString());
            }

            request = new UserPresenceUpdateRequest(isOnlineElement.GetBoolean(), clientTimestamp);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool TryGetBoolean(object? value, out bool result)
    {
        switch (value)
        {
            case bool boolean:
                result = boolean;
                return true;
            case string text when bool.TryParse(text, out var parsed):
                result = parsed;
                return true;
            default:
                result = false;
                return false;
        }
    }

    private static DateTime? ParseClientTimestamp(object? value)
    {
        return value switch
        {
            null => null,
            DateTime dateTime => dateTime.Kind == DateTimeKind.Utc ? dateTime : dateTime.ToUniversalTime(),
            DateTimeOffset dateTimeOffset => dateTimeOffset.UtcDateTime,
            string raw => ParseClientTimestamp(raw),
            long unixMilliseconds => DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds).UtcDateTime,
            int unixMilliseconds => DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds).UtcDateTime,
            _ => null
        };
    }

    private static DateTime? ParseClientTimestamp(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

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
}
