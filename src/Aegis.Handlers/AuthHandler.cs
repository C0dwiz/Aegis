using Aegis.Protocol;
using Aegis.Transport;
using Aegis.Common;
using Aegis.Data.Repositories;
using Aegis.Data.Services;
using Microsoft.Extensions.Logging;

namespace Aegis.Handlers;

public class AuthRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public string ClientInfo { get; set; } = string.Empty;
}

public class AuthResponse
{
    public bool Success { get; set; }
    public ulong UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string SessionToken { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
}

public class AuthHandler : IMessageHandler
{
    private readonly IUserAuthenticationService _authService;
    private readonly IRateLimiter _rateLimiter;
    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;
    private readonly IMessageRepository _messageRepository;
    private readonly ISessionRepository? _sessionRepository;
    private readonly Func<IEnumerable<ulong>, Task<IDictionary<ulong, string>>> _getUsernamesByIds;
    private readonly ILogger<AuthHandler> _logger;

    public MessageType Type => MessageType.Auth;

    public AuthHandler(
        IUserAuthenticationService authService,
        IRateLimiter rateLimiter,
        SessionManager sessionManager,
        IMessageSender messageSender,
        IMessageRepository messageRepository,
        ISessionRepository sessionRepository,
        IUserRepository userRepository,
        ILogger<AuthHandler> logger)
    {
        _authService = authService;
        _rateLimiter = rateLimiter;
        _sessionManager = sessionManager;
        _messageSender = messageSender;
        _messageRepository = messageRepository;
        _sessionRepository = sessionRepository;
        _getUsernamesByIds = userRepository.GetUsernamesByIdsAsync;
        _logger = logger;
    }

    public AuthHandler(
        IUserAuthenticationService authService,
        RateLimiter rateLimiter,
        SessionManager sessionManager,
        IMessageSender messageSender,
        IMessageRepository messageRepository,
        IUserSearchService userSearchService,
        ILogger<AuthHandler> logger)
    {
        _authService = authService;
        _rateLimiter = rateLimiter;
        _sessionManager = sessionManager;
        _messageSender = messageSender;
        _messageRepository = messageRepository;
        _sessionRepository = null;
        _getUsernamesByIds = async ids =>
        {
            var result = new Dictionary<ulong, string>();
            foreach (var id in ids.Distinct())
            {
                var user = await userSearchService.FindUserByIdAsync(id);
                if (user != null)
                {
                    result[id] = user.Username;
                }
            }
            return result;
        };
        _logger = logger;
    }

    public async ValueTask HandleAsync(ConnectionContext context, Message message)
    {
        try
        {
            if (!_rateLimiter.CanSendAuthRequest(context.ConnectionId))
            {
                await SendAuthResponseAsync(context, message.SequenceId, new AuthResponse
                {
                    Success = false,
                    Error = "Too many authentication attempts"
                });
                return;
            }

            var authRequest = PayloadSerializer.Deserialize<AuthRequest>(message.Payload);
            if (authRequest == null)
            {
                await SendAuthResponseAsync(context, message.SequenceId, new AuthResponse
                {
                    Success = false,
                    Error = "Invalid authentication request format"
                });
                return;
            }

            _logger.LogInformation("Authentication attempt for user: {Username} from connection {ConnectionId}",
                authRequest.Username, context.ConnectionId);

            // Token-based re-authentication
            if (!string.IsNullOrEmpty(authRequest.Token))
            {
                var session = await _authService.AuthenticateUserByTokenAsync(authRequest.Token);
                if (session?.User != null)
                {
                    if (!_sessionManager.AuthenticateSession(context.ConnectionId, session.UserId, session.User.Username))
                    {
                        await SendAuthResponseAsync(context, message.SequenceId, new AuthResponse
                        {
                            Success = false,
                            Error = "Handshake required before authentication"
                        });
                        return;
                    }

                    if (_sessionRepository != null)
                    {
                        session.ConnectionId = context.ConnectionId.ToString();
                        await _sessionRepository.UpdateAsync(session);
                    }

                    await SendAuthResponseAsync(context, message.SequenceId, new AuthResponse
                    {
                        Success = true,
                        UserId = session.UserId,
                        Username = session.User.Username,
                        SessionToken = session.SessionToken
                    });

                    await DeliverUndeliveredMessagesAsync(context.ConnectionId, session.UserId);
                    return;
                }
                await SendAuthResponseAsync(context, message.SequenceId, new AuthResponse
                {
                    Success = false,
                    Error = "Authentication failed"
                });
                _logger.LogWarning("Token authentication failed on connection {ConnectionId}", context.ConnectionId);
                return;
            }

            // Username/password authentication
            var ipAddress = context.Socket.RemoteEndPoint?.ToString();
            var result = await _authService.AuthenticateUserAsync(
                authRequest.Username,
                authRequest.Password,
                authRequest.ClientInfo,
                ipAddress);

            if (result == null)
            {
                _logger.LogWarning("Authentication failed for user {Username} from connection {ConnectionId}",
                    authRequest.Username, context.ConnectionId);
                await SendAuthResponseAsync(context, message.SequenceId, new AuthResponse
                {
                    Success = false,
                    Error = "Authentication failed"
                });
                return;
            }

            var (user, dbSession) = result.Value;

            // Associate the TCP connection with the authenticated user
            if (!_sessionManager.AuthenticateSession(context.ConnectionId, user.Id, user.Username))
            {
                await SendAuthResponseAsync(context, message.SequenceId, new AuthResponse
                {
                    Success = false,
                    Error = "Handshake required before authentication"
                });
                return;
            }

            // Bind session to connection
            dbSession.ConnectionId = context.ConnectionId.ToString();
            if (_sessionRepository != null)
            {
                await _sessionRepository.UpdateAsync(dbSession);
            }

            _logger.LogInformation("User {Username} (ID: {UserId}) authenticated successfully from connection {ConnectionId}",
                user.Username, user.Id, context.ConnectionId);

            await SendAuthResponseAsync(context, message.SequenceId, new AuthResponse
            {
                Success = true,
                UserId = user.Id,
                Username = user.Username,
                SessionToken = dbSession.SessionToken
            });

            await DeliverUndeliveredMessagesAsync(context.ConnectionId, user.Id);
        }
        catch (Exception ex)
        {
            _logger.LogHandlerError(ex, "auth_process", context, message, _sessionManager);
            await SendAuthResponseAsync(context, message.SequenceId, new AuthResponse
            {
                Success = false,
                Error = "Internal server error"
            });
        }
    }

    private async Task DeliverUndeliveredMessagesAsync(ulong connectionId, ulong userId)
    {
        try
        {
            var undelivered = (await _messageRepository.GetUndeliveredMessagesAsync(userId)).ToList();
            if (undelivered.Count == 0)
            {
                return;
            }

            var senderNames = await _getUsernamesByIds(undelivered.Select(m => m.FromUserId));

            foreach (var message in undelivered.OrderBy(m => m.CreatedAt))
            {
                var fromUsername = senderNames.TryGetValue(message.FromUserId, out var resolvedUsername)
                    ? resolvedUsername
                    : null;

                var payload = PayloadSerializer.Serialize(new PrivateChatMessageEventPayload(
                    Id: message.Id,
                    FromUserId: message.FromUserId,
                    ToUserId: message.ToUserId,
                    Content: message.Content,
                    ContentType: message.ContentType,
                    CreatedAt: message.CreatedAt,
                    DeliveredTo: message.IsDelivered ? new List<ulong> { message.ToUserId } : new List<ulong>(),
                    ReadBy: message.IsRead ? new List<ulong> { message.ToUserId } : new List<ulong>(),
                    FromUsername: fromUsername,
                    Username: fromUsername));

                await _messageSender.SendProtocolMessageAsync(
                    connectionId,
                    (ushort)MessageType.PrivateChatMessageEvent,
                    0,
                    payload);
            }

            await _messageRepository.MarkMessagesDeliveredAsync(undelivered.Select(x => x.Id), userId);
            _logger.LogInformation("Delivered {Count} pending messages to user {UserId}", undelivered.Count, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "handler_error op={Operation} conn={ConnectionId} user={UserId}", "auth_deliver_pending", connectionId, userId);
        }
    }

    private async Task SendAuthResponseAsync(ConnectionContext context, ulong sequenceId, AuthResponse response)
    {
        try
        {
            var responseJson = PayloadSerializer.Serialize(response);
            await _messageSender.SendProtocolMessageAsync(
                context.ConnectionId,
                (ushort)MessageType.Ack,
                sequenceId,
                responseJson);
        }
        catch (Exception ex)
        {
            _logger.LogHandlerError(ex, "auth_send_response", context, sequenceId: sequenceId);
        }
    }
}
