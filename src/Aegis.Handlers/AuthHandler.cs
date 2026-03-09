using Aegis.Protocol;
using Aegis.Transport;
using Aegis.Common;
using Aegis.Data.Repositories;
using Aegis.Data.Services;
using System.Text.Json;
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
    private readonly RateLimiter _rateLimiter;
    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;
    private readonly IMessageRepository _messageRepository;
    private readonly IUserSearchService _userSearchService;
    private readonly ILogger<AuthHandler> _logger;
    
    public MessageType Type => MessageType.Auth;
    
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
        _userSearchService = userSearchService;
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

            var authRequest = JsonSerializer.Deserialize<AuthRequest>(message.Payload);
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
                    _sessionManager.AuthenticateSession(context.ConnectionId, session.UserId, session.User.Username);
                    await SendAuthResponseAsync(context, message.SequenceId, new AuthResponse
                    {
                        Success = true,
                        UserId = session.UserId,
                        Username = session.User.Username,
                        SessionToken = string.Empty
                    });

                    await DeliverUndeliveredMessagesAsync(context.ConnectionId, session.UserId);
                    return;
                }
                await SendAuthResponseAsync(context, message.SequenceId, new AuthResponse
                {
                    Success = false,
                    Error = "Authentication failed"
                });
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
            _sessionManager.AuthenticateSession(context.ConnectionId, user.Id, user.Username);
            
            // Bind session to connection
            dbSession.ConnectionId = context.ConnectionId.ToString();
            
            _logger.LogInformation("User {Username} (ID: {UserId}) authenticated successfully from connection {ConnectionId}", 
                user.Username, user.Id, context.ConnectionId);
            
            await SendAuthResponseAsync(context, message.SequenceId, new AuthResponse
            {
                Success = true,
                UserId = user.Id,
                Username = user.Username,
                SessionToken = string.Empty
            });

            await DeliverUndeliveredMessagesAsync(context.ConnectionId, user.Id);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Invalid JSON in auth message from connection {ConnectionId}", context.ConnectionId);
            await SendAuthResponseAsync(context, message.SequenceId, new AuthResponse
            {
                Success = false,
                Error = "Invalid JSON format"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing authentication from connection {ConnectionId}", context.ConnectionId);
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

            var senderNames = new Dictionary<ulong, string>();
            foreach (var senderId in undelivered.Select(m => m.FromUserId).Distinct())
            {
                var sender = await _userSearchService.FindUserByIdAsync(senderId);
                if (sender != null)
                {
                    senderNames[senderId] = sender.Username;
                }
            }

            foreach (var message in undelivered.OrderBy(m => m.CreatedAt))
            {
                var payload = JsonSerializer.SerializeToUtf8Bytes(new PrivateChatMessageEventPayload(
                    Id: message.Id,
                    FromUserId: message.FromUserId,
                    ToUserId: message.ToUserId,
                    Content: message.Content,
                    ContentType: message.ContentType,
                    CreatedAt: message.CreatedAt,
                    FromUsername: senderNames.GetValueOrDefault(message.FromUserId),
                    Username: senderNames.GetValueOrDefault(message.FromUserId)));

                await _messageSender.SendProtocolMessageAsync(
                    connectionId,
                    (ushort)MessageType.PrivateChatMessageEvent,
                    0,
                    payload);
            }

            await _messageRepository.MarkMessagesDeliveredAsync(undelivered.Select(x => x.Id));
            _logger.LogInformation("Delivered {Count} pending messages to user {UserId}", undelivered.Count, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deliver pending messages to user {UserId}", userId);
        }
    }
    
    private async Task SendAuthResponseAsync(ConnectionContext context, ulong sequenceId, AuthResponse response)
    {
        try
        {
            var responseJson = JsonSerializer.SerializeToUtf8Bytes(response);
            
            var responseMessage = new Aegis.Protocol.Message
            {
                Magic = ProtocolConstants.Magic,
                VersionMajor = ProtocolConstants.VersionMajor,
                VersionMinor = ProtocolConstants.VersionMinor,
                Type = MessageType.Ack,
                SequenceId = sequenceId,
                PayloadLength = (uint)responseJson.Length,
                Payload = responseJson
            };

            await _messageSender.SendProtocolMessageAsync(
                context.ConnectionId,
                (ushort)MessageType.Ack,
                sequenceId,
                responseJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending auth response to connection {ConnectionId}", context.ConnectionId);
        }
    }
}
