using System.Text.Json;
using Aegis.Common;
using Aegis.Data.Services;
using Aegis.Protocol;
using Aegis.Transport;
using Microsoft.Extensions.Logging;

namespace Aegis.Handlers;

// ===================== PAYLOADS =====================

public record ProfileUpdateRequest(
    string? DisplayName = null,
    string? AvatarUrl = null,
    string? Bio = null,
    string? Username = null
);

public record ProfileUpdateResponse(
    bool Success,
    string? Message = null,
    ProfileData? Profile = null
);

public record ProfileGetRequest(
    ulong? UserId = null,
    string? Username = null
);

public record ProfileGetResponse(
    bool Success,
    ProfileData? Profile = null,
    string? Message = null
);

public record ProfileData(
    ulong Id,
    string Username,
    string? DisplayName,
    string? AvatarUrl,
    string? Bio,
    string? Email,
    DateTime CreatedAt,
    DateTime? LastSeenAt
);

// ===================== PROFILE UPDATE HANDLER =====================

public class ProfileUpdateHandler : IMessageHandler
{
    public MessageType Type => MessageType.ProfileUpdate;

    private readonly IUserProfileService _profileService;
    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;
    private readonly ILogger<ProfileUpdateHandler> _logger;

    public ProfileUpdateHandler(
        IUserProfileService profileService,
        SessionManager sessionManager,
        IMessageSender messageSender,
        ILogger<ProfileUpdateHandler> logger)
    {
        _profileService = profileService;
        _sessionManager = sessionManager;
        _messageSender = messageSender;
        _logger = logger;
    }

    public async ValueTask HandleAsync(ConnectionContext context, Message message)
    {
        try
        {
            var session = _sessionManager.GetAuthenticatedSession(context.ConnectionId);
            if (session == null)
            {
                await SendResponseAsync(context, message.SequenceId, new ProfileUpdateResponse(false, Message: "Not authenticated"));
                return;
            }

            var request = JsonSerializer.Deserialize<ProfileUpdateRequest>(message.Payload);
            if (request == null)
            {
                await SendResponseAsync(context, message.SequenceId, new ProfileUpdateResponse(false, Message: "Invalid payload"));
                return;
            }

            var user = await _profileService.UpdateProfileAsync(
                session.UserId,
                request.DisplayName,
                request.AvatarUrl,
                request.Bio,
                request.Username);

            var profileData = new ProfileData(
                user.Id, user.Username, user.DisplayName,
                user.AvatarUrl, user.Bio, user.Email,
                user.CreatedAt, user.LastSeenAt);

            await SendResponseAsync(context, message.SequenceId, 
                new ProfileUpdateResponse(true, Message: "Profile updated", Profile: profileData));

            _logger.LogInformation("Profile updated for user {UserId}", session.UserId);
        }
        catch (InvalidOperationException ex)
        {
            await SendResponseAsync(context, message.SequenceId, new ProfileUpdateResponse(false, Message: ex.Message));
        }
        catch (ArgumentException ex)
        {
            await SendResponseAsync(context, message.SequenceId, new ProfileUpdateResponse(false, Message: ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating profile");
            await SendResponseAsync(context, message.SequenceId, new ProfileUpdateResponse(false, Message: "Internal server error"));
        }
    }

    private async Task SendResponseAsync(ConnectionContext context, ulong sequenceId, ProfileUpdateResponse response)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(response);
        var msg = new Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = ProtocolConstants.VersionMajor,
            VersionMinor = ProtocolConstants.VersionMinor,
            Type = MessageType.ProfileUpdateResponse,
            SequenceId = sequenceId,
            PayloadLength = (uint)payload.Length,
            Payload = payload,
            Mac = new byte[ProtocolConstants.MacSize]
        };
        var buffer = new byte[ProtocolConstants.HeaderSize + payload.Length + ProtocolConstants.MacSize];
        MessageEncoder.Encode(msg, buffer);
        await _messageSender.SendMessageAsync(context.ConnectionId, buffer);
    }
}

// ===================== PROFILE GET HANDLER =====================

public class ProfileGetHandler : IMessageHandler
{
    public MessageType Type => MessageType.ProfileGet;

    private readonly IUserProfileService _profileService;
    private readonly IUserSearchService _searchService;
    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;
    private readonly ILogger<ProfileGetHandler> _logger;

    public ProfileGetHandler(
        IUserProfileService profileService,
        IUserSearchService searchService,
        SessionManager sessionManager,
        IMessageSender messageSender,
        ILogger<ProfileGetHandler> logger)
    {
        _profileService = profileService;
        _searchService = searchService;
        _sessionManager = sessionManager;
        _messageSender = messageSender;
        _logger = logger;
    }

    public async ValueTask HandleAsync(ConnectionContext context, Message message)
    {
        try
        {
            var session = _sessionManager.GetAuthenticatedSession(context.ConnectionId);
            if (session == null)
            {
                await SendResponseAsync(context, message.SequenceId, new ProfileGetResponse(false, Message: "Not authenticated"));
                return;
            }

            var request = JsonSerializer.Deserialize<ProfileGetRequest>(message.Payload);

            // If no ID/username specified, return own profile
            ulong targetUserId = session.UserId;
            if (request?.UserId != null && request.UserId > 0)
            {
                targetUserId = request.UserId.Value;
            }
            else if (!string.IsNullOrEmpty(request?.Username))
            {
                var found = await _searchService.FindUserByUsernameAsync(request.Username);
                if (found == null)
                {
                    await SendResponseAsync(context, message.SequenceId, new ProfileGetResponse(false, Message: "User not found"));
                    return;
                }
                targetUserId = found.Id;
            }

            var user = await _profileService.GetProfileAsync(targetUserId);
            if (user == null)
            {
                await SendResponseAsync(context, message.SequenceId, new ProfileGetResponse(false, Message: "User not found"));
                return;
            }

            // Hide email if viewing someone else's profile
            var email = targetUserId == session.UserId ? user.Email : null;

            var profileData = new ProfileData(
                user.Id, user.Username, user.DisplayName,
                user.AvatarUrl, user.Bio, email,
                user.CreatedAt, user.LastSeenAt);

            await SendResponseAsync(context, message.SequenceId, new ProfileGetResponse(true, Profile: profileData));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting profile");
            await SendResponseAsync(context, message.SequenceId, new ProfileGetResponse(false, Message: "Internal server error"));
        }
    }

    private async Task SendResponseAsync(ConnectionContext context, ulong sequenceId, ProfileGetResponse response)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(response);
        var msg = new Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = ProtocolConstants.VersionMajor,
            VersionMinor = ProtocolConstants.VersionMinor,
            Type = MessageType.ProfileGetResponse,
            SequenceId = sequenceId,
            PayloadLength = (uint)payload.Length,
            Payload = payload,
            Mac = new byte[ProtocolConstants.MacSize]
        };
        var buffer = new byte[ProtocolConstants.HeaderSize + payload.Length + ProtocolConstants.MacSize];
        MessageEncoder.Encode(msg, buffer);
        await _messageSender.SendMessageAsync(context.ConnectionId, buffer);
    }
}
