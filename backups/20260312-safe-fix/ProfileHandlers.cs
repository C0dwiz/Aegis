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
    IReadOnlyList<ProfileAvatarData> Avatars,
    string PresenceStatus = UserPresenceStatus.LongAgo,
    string? Bio = null,
    string? Email = null,
    DateTime CreatedAt = default,
    DateTime? LastSeenAt = null
);

public record ProfileAvatarData(
    ulong Id,
    string AvatarUrl,
    bool IsPrimary,
    DateTime CreatedAt
);

public record ProfileAvatarAddRequest(
    string AvatarUrl,
    bool MakePrimary = false
);

public record ProfileAvatarMutationResponse(
    bool Success,
    string? Message = null,
    ProfileAvatarData? Avatar = null
);

public record ProfileAvatarListResponse(
    bool Success,
    IReadOnlyList<ProfileAvatarData>? Avatars = null,
    string? Message = null
);

public record ProfileAvatarDeleteRequest(ulong AvatarId);
public record ProfileAvatarSetPrimaryRequest(ulong AvatarId);

// ===================== PROFILE UPDATE HANDLER =====================

public class ProfileUpdateHandler : IMessageHandler
{
    public MessageType Type => MessageType.ProfileUpdate;

    private readonly IUserProfileService _profileService;
    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;
    private readonly ILogger<ProfileUpdateHandler> _logger;
    private readonly UserPresenceResolver _presenceResolver;

    public ProfileUpdateHandler(
        IUserProfileService profileService,
        SessionManager sessionManager,
        IMessageSender messageSender,
        ILogger<ProfileUpdateHandler> logger,
        UserPresenceResolver presenceResolver)
    {
        _profileService = profileService;
        _sessionManager = sessionManager;
        _messageSender = messageSender;
        _logger = logger;
        _presenceResolver = presenceResolver;
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
                user.AvatarUrl,
                (await _profileService.GetAvatarsAsync(user.Id))
                    .Select(a => new ProfileAvatarData(a.Id, a.AvatarUrl, a.IsPrimary, a.CreatedAt))
                    .ToList(),
                _presenceResolver.Resolve(user.Id, user.LastSeenAt),
                user.Bio, user.Email,
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
            _logger.LogHandlerError(ex, "profile_update", context, message, _sessionManager);
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
    private readonly UserPresenceResolver _presenceResolver;

    public ProfileGetHandler(
        IUserProfileService profileService,
        IUserSearchService searchService,
        SessionManager sessionManager,
        IMessageSender messageSender,
        ILogger<ProfileGetHandler> logger,
        UserPresenceResolver presenceResolver)
    {
        _profileService = profileService;
        _searchService = searchService;
        _sessionManager = sessionManager;
        _messageSender = messageSender;
        _logger = logger;
        _presenceResolver = presenceResolver;
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
                user.AvatarUrl,
                (await _profileService.GetAvatarsAsync(user.Id))
                    .Select(a => new ProfileAvatarData(a.Id, a.AvatarUrl, a.IsPrimary, a.CreatedAt))
                    .ToList(),
                _presenceResolver.Resolve(user.Id, user.LastSeenAt),
                user.Bio, email,
                user.CreatedAt, user.LastSeenAt);

            await SendResponseAsync(context, message.SequenceId, new ProfileGetResponse(true, Profile: profileData));
        }
        catch (Exception ex)
        {
            _logger.LogHandlerError(ex, "profile_get", context, message, _sessionManager);
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

public class ProfileAvatarAddHandler : IMessageHandler
{
    public MessageType Type => MessageType.ProfileAvatarAdd;

    private readonly IUserProfileService _profileService;
    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;

    public ProfileAvatarAddHandler(
        IUserProfileService profileService,
        SessionManager sessionManager,
        IMessageSender messageSender)
    {
        _profileService = profileService;
        _sessionManager = sessionManager;
        _messageSender = messageSender;
    }

    public async ValueTask HandleAsync(ConnectionContext context, Message message)
    {
        var session = _sessionManager.GetAuthenticatedSession(context.ConnectionId);
        if (session == null)
        {
            await SendAsync(context, message.SequenceId, new ProfileAvatarMutationResponse(false, "Not authenticated"));
            return;
        }

        var request = JsonSerializer.Deserialize<ProfileAvatarAddRequest>(message.Payload);
        if (request == null || string.IsNullOrWhiteSpace(request.AvatarUrl))
        {
            await SendAsync(context, message.SequenceId, new ProfileAvatarMutationResponse(false, "Invalid payload"));
            return;
        }

        try
        {
            var avatar = await _profileService.AddAvatarAsync(session.UserId, request.AvatarUrl, request.MakePrimary);
            await SendAsync(context, message.SequenceId, new ProfileAvatarMutationResponse(
                true,
                "Avatar added",
                new ProfileAvatarData(avatar.Id, avatar.AvatarUrl, avatar.IsPrimary, avatar.CreatedAt)));
        }
        catch (Exception ex)
        {
            await SendAsync(context, message.SequenceId, new ProfileAvatarMutationResponse(false, ex.Message));
        }
    }

    private Task SendAsync(ConnectionContext context, ulong sequenceId, ProfileAvatarMutationResponse response)
    {
        return _messageSender.SendProtocolMessageAsync(
            context.ConnectionId,
            (ushort)MessageType.ProfileAvatarAddResponse,
            sequenceId,
            JsonSerializer.SerializeToUtf8Bytes(response));
    }
}

public class ProfileAvatarListHandler : IMessageHandler
{
    public MessageType Type => MessageType.ProfileAvatarList;

    private readonly IUserProfileService _profileService;
    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;

    public ProfileAvatarListHandler(
        IUserProfileService profileService,
        SessionManager sessionManager,
        IMessageSender messageSender)
    {
        _profileService = profileService;
        _sessionManager = sessionManager;
        _messageSender = messageSender;
    }

    public async ValueTask HandleAsync(ConnectionContext context, Message message)
    {
        var session = _sessionManager.GetAuthenticatedSession(context.ConnectionId);
        if (session == null)
        {
            await SendAsync(context, message.SequenceId, new ProfileAvatarListResponse(false, Message: "Not authenticated"));
            return;
        }

        var avatars = await _profileService.GetAvatarsAsync(session.UserId);
        await SendAsync(context, message.SequenceId, new ProfileAvatarListResponse(
            true,
            avatars.Select(a => new ProfileAvatarData(a.Id, a.AvatarUrl, a.IsPrimary, a.CreatedAt)).ToList()));
    }

    private Task SendAsync(ConnectionContext context, ulong sequenceId, ProfileAvatarListResponse response)
    {
        return _messageSender.SendProtocolMessageAsync(
            context.ConnectionId,
            (ushort)MessageType.ProfileAvatarListResponse,
            sequenceId,
            JsonSerializer.SerializeToUtf8Bytes(response));
    }
}

public class ProfileAvatarDeleteHandler : IMessageHandler
{
    public MessageType Type => MessageType.ProfileAvatarDelete;

    private readonly IUserProfileService _profileService;
    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;

    public ProfileAvatarDeleteHandler(
        IUserProfileService profileService,
        SessionManager sessionManager,
        IMessageSender messageSender)
    {
        _profileService = profileService;
        _sessionManager = sessionManager;
        _messageSender = messageSender;
    }

    public async ValueTask HandleAsync(ConnectionContext context, Message message)
    {
        var session = _sessionManager.GetAuthenticatedSession(context.ConnectionId);
        if (session == null)
        {
            await SendAsync(context, message.SequenceId, false, "Not authenticated");
            return;
        }

        var request = JsonSerializer.Deserialize<ProfileAvatarDeleteRequest>(message.Payload);
        if (request == null || request.AvatarId == 0)
        {
            await SendAsync(context, message.SequenceId, false, "Invalid payload");
            return;
        }

        var deleted = await _profileService.DeleteAvatarAsync(session.UserId, request.AvatarId);
        await SendAsync(context, message.SequenceId, deleted, deleted ? "Avatar deleted" : "Avatar not found");
    }

    private Task SendAsync(ConnectionContext context, ulong sequenceId, bool success, string message)
    {
        return _messageSender.SendProtocolMessageAsync(
            context.ConnectionId,
            (ushort)MessageType.ProfileAvatarDeleteResponse,
            sequenceId,
            JsonSerializer.SerializeToUtf8Bytes(new ProfileAvatarMutationResponse(success, message)));
    }
}

public class ProfileAvatarSetPrimaryHandler : IMessageHandler
{
    public MessageType Type => MessageType.ProfileAvatarSetPrimary;

    private readonly IUserProfileService _profileService;
    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;

    public ProfileAvatarSetPrimaryHandler(
        IUserProfileService profileService,
        SessionManager sessionManager,
        IMessageSender messageSender)
    {
        _profileService = profileService;
        _sessionManager = sessionManager;
        _messageSender = messageSender;
    }

    public async ValueTask HandleAsync(ConnectionContext context, Message message)
    {
        var session = _sessionManager.GetAuthenticatedSession(context.ConnectionId);
        if (session == null)
        {
            await SendAsync(context, message.SequenceId, false, "Not authenticated");
            return;
        }

        var request = JsonSerializer.Deserialize<ProfileAvatarSetPrimaryRequest>(message.Payload);
        if (request == null || request.AvatarId == 0)
        {
            await SendAsync(context, message.SequenceId, false, "Invalid payload");
            return;
        }

        var updated = await _profileService.SetPrimaryAvatarAsync(session.UserId, request.AvatarId);
        await SendAsync(context, message.SequenceId, updated, updated ? "Primary avatar updated" : "Avatar not found");
    }

    private Task SendAsync(ConnectionContext context, ulong sequenceId, bool success, string message)
    {
        return _messageSender.SendProtocolMessageAsync(
            context.ConnectionId,
            (ushort)MessageType.ProfileAvatarSetPrimaryResponse,
            sequenceId,
            JsonSerializer.SerializeToUtf8Bytes(new ProfileAvatarMutationResponse(success, message)));
    }
}
