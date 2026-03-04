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
    User? User = null
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
    string Username,
    string? Email
);

/// <summary>
/// Registration handler
/// </summary>
public class RegistrationHandler : IMessageHandler
{
    public MessageType Type => MessageType.Register;
    
    private readonly IUserRegistrationService _registrationService;
    private readonly ILogger<RegistrationHandler> _logger;

    public RegistrationHandler(IUserRegistrationService registrationService, ILogger<RegistrationHandler> logger)
    {
        _registrationService = registrationService;
        _logger = logger;
    }

    public async ValueTask HandleAsync(ConnectionContext context, Aegis.Protocol.Message message)
    {
        try
        {
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
                User: user
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

        // This would need to be sent through the message sender
        // For now, we'll just log it
        _logger.LogDebug("Registration response sent for sequence {SequenceId}", sequenceId);
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
    private readonly ILogger<UserSearchHandler> _logger;

    public UserSearchHandler(IUserSearchService searchService, ILogger<UserSearchHandler> logger)
    {
        _searchService = searchService;
        _logger = logger;
    }

    public async ValueTask HandleAsync(ConnectionContext context, Aegis.Protocol.Message message)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<UserSearchRequest>(message.Payload);
            if (payload == null)
            {
                await SendErrorResponse(context, message.SequenceId, "Invalid payload");
                return;
            }

            var users = await _searchService.SearchUsersByUsernameAsync(payload.Query, payload.Limit);
            var searchResults = users.Select(u => new UserSearchResult(u.Id, u.Username, u.Email)).ToList();

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

        _logger.LogDebug("User search response sent for sequence {SequenceId}", sequenceId);
    }

    private async Task SendErrorResponse(ConnectionContext context, ulong sequenceId, string errorMessage)
    {
        var response = new UserSearchResponse(Success: false, Users: new List<UserSearchResult>(), Message: errorMessage);
        await SendResponseAsync(context, sequenceId, response);
    }
}
