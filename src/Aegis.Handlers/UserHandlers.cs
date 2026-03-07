using System.Text.Json;
using Aegis.Common;
using Aegis.Data.Entities;
using Aegis.Data.Repositories;
using Aegis.Data.Services;
using Aegis.Protocol;
using Aegis.Transport;
using Microsoft.Extensions.Logging;

namespace Aegis.Handlers;

/// <summary>
/// Registration request payload
/// </summary>
public record RegistrationRequest(
    string Username,
    string Email,
    string Password,
    string PublicKey
);

/// <summary>
/// Registration response payload
/// </summary>
public record RegistrationResponse(
    bool Success,
    string? Message = null,
    RegisteredUserInfo? User = null
);

public record RegisteredUserInfo(
    ulong Id,
    string Username
);

/// <summary>
/// User search request payload
/// </summary>
public record UserSearchRequest(
    string Query,
    int Limit = 20
);

/// <summary>
/// User search response payload
/// </summary>
public record UserSearchResponse(
    bool Success,
    List<UserSearchResult> Users,
    string? Message = null
);

/// <summary>
/// User search result item
/// </summary>
public record UserSearchResult(
    ulong Id,
    string Username
);

/// <summary>
/// Registration handler
/// </summary>
public class RegistrationHandler : IMessageHandler
{
    public MessageType Type => MessageType.Register;
    
    private readonly IUserRegistrationService _registrationService;
    private readonly IMessageSender _messageSender;
    private readonly RateLimiter _rateLimiter;
    private readonly ILogger<RegistrationHandler> _logger;

    public RegistrationHandler(
        IUserRegistrationService registrationService,
        IMessageSender messageSender,
        RateLimiter rateLimiter,
        ILogger<RegistrationHandler> logger)
    {
        _registrationService = registrationService;
        _messageSender = messageSender;
        _rateLimiter = rateLimiter;
        _logger = logger;
    }

    public async ValueTask HandleAsync(ConnectionContext context, Aegis.Protocol.Message message)
    {
        try
        {
            if (!_rateLimiter.CanSendAuthRequest(context.ConnectionId))
            {
                await SendErrorResponse(context, message.SequenceId, "Too many registration attempts");
                return;
            }

            var payload = JsonSerializer.Deserialize<RegistrationRequest>(message.Payload);
            if (payload == null)
            {
                await SendErrorResponse(context, message.SequenceId, "Invalid payload");
                return;
            }

            var user = await _registrationService.RegisterUserAsync(
                payload.Username,
                payload.Email,
                payload.Password,
                payload.PublicKey
            );

            var response = new RegistrationResponse(
                Success: true,
                User: new RegisteredUserInfo(user.Id, user.Username)
            );

            await SendResponseAsync(context, message.SequenceId, response);
            _logger.LogInformation("User {Username} registered successfully", payload.Username);
            return;
        }
        catch (ArgumentException ex)
        {
            await SendErrorResponse(context, message.SequenceId, ex.Message);
            return;
        }
        catch (InvalidOperationException ex)
        {
            await SendErrorResponse(context, message.SequenceId, ex.Message);
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during user registration");
            await SendErrorResponse(context, message.SequenceId, "Internal server error");
            return;
        }
    }

    private async Task SendResponseAsync(ConnectionContext context, ulong sequenceId, RegistrationResponse response)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(response);
        var responseMessage = new Aegis.Protocol.Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = ProtocolConstants.VersionMajor,
            VersionMinor = ProtocolConstants.VersionMinor,
            Type = MessageType.RegisterResponse,
            SequenceId = sequenceId,
            PayloadLength = (uint)payload.Length,
            Payload = payload
        };

        await _messageSender.SendProtocolMessageAsync(
            context.ConnectionId,
            (ushort)MessageType.RegisterResponse,
            sequenceId,
            payload);
    }

    private async Task SendErrorResponse(ConnectionContext context, ulong sequenceId, string errorMessage)
    {
        var response = new RegistrationResponse(Success: false, Message: errorMessage);
        await SendResponseAsync(context, sequenceId, response);
    }
}

/// <summary>
/// User search handler
/// </summary>
public class UserSearchHandler : IMessageHandler
{
    public MessageType Type => MessageType.UserSearch;
    
    private readonly IUserSearchService _searchService;
    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;
    private readonly RateLimiter _rateLimiter;
    private readonly ILogger<UserSearchHandler> _logger;

    public UserSearchHandler(
        IUserSearchService searchService,
        SessionManager sessionManager,
        IMessageSender messageSender,
        RateLimiter rateLimiter,
        ILogger<UserSearchHandler> logger)
    {
        _searchService = searchService;
        _sessionManager = sessionManager;
        _messageSender = messageSender;
        _rateLimiter = rateLimiter;
        _logger = logger;
    }

    public async ValueTask HandleAsync(ConnectionContext context, Aegis.Protocol.Message message)
    {
        try
        {
            var session = _sessionManager.GetAuthenticatedSession(context.ConnectionId);
            if (session == null)
            {
                await SendErrorResponse(context, message.SequenceId, "Not authenticated");
                return;
            }

            if (!_rateLimiter.CanSendMessage(context.ConnectionId))
            {
                await SendErrorResponse(context, message.SequenceId, "Rate limit exceeded");
                return;
            }

            var payload = JsonSerializer.Deserialize<UserSearchRequest>(message.Payload);
            if (payload == null)
            {
                await SendErrorResponse(context, message.SequenceId, "Invalid payload");
                return;
            }

            var safeLimit = Math.Clamp(payload.Limit, 1, 20);
            var users = await _searchService.SearchUsersByUsernameAsync(payload.Query, safeLimit);
            var searchResults = users.Select(u => new UserSearchResult(u.Id, u.Username)).ToList();

            var response = new UserSearchResponse(
                Success: true,
                Users: searchResults,
                Message: "Search completed successfully"
            );

            await SendResponseAsync(context, message.SequenceId, response);
            _logger.LogInformation("User search for '{Query}' returned {Count} results", payload.Query, searchResults.Count);
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during user search");
            await SendErrorResponse(context, message.SequenceId, "Internal server error");
            return;
        }
    }

    private async Task SendResponseAsync(ConnectionContext context, ulong sequenceId, UserSearchResponse response)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(response);
        var responseMessage = new Aegis.Protocol.Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = ProtocolConstants.VersionMajor,
            VersionMinor = ProtocolConstants.VersionMinor,
            Type = MessageType.UserSearchResult,
            SequenceId = sequenceId,
            PayloadLength = (uint)payload.Length,
            Payload = payload
        };

        await _messageSender.SendProtocolMessageAsync(
            context.ConnectionId,
            (ushort)MessageType.UserSearchResult,
            sequenceId,
            payload);
    }

    private async Task SendErrorResponse(ConnectionContext context, ulong sequenceId, string errorMessage)
    {
        var response = new UserSearchResponse(Success: false, Users: new List<UserSearchResult>(), Message: errorMessage);
        await SendResponseAsync(context, sequenceId, response);
    }
}
