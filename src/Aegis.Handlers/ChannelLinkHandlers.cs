using Aegis.Common;
using Aegis.Data.Services;
using Aegis.Protocol;
using Aegis.Transport;

namespace Aegis.Handlers;

public record ChannelLinkUpdateRequest(
    ulong ChannelId,
    string? PublicAlias = null,
    bool RegeneratePrivateInvite = false
);

public record ChannelLinkRequest(
    ulong ChannelId
);

public record ChannelResolveRequest(
    string LinkOrAlias
);

public record ChannelLinkInfo(
    ulong ChannelId,
    string? PublicAlias,
    string? PublicLink,
    string PrivateInviteLink
);

public record ChannelLinkResponse(
    bool Success,
    ChannelLinkInfo? Link = null,
    string? Message = null
);

public record ChannelResolveResponse(
    bool Success,
    ChannelSummary? Channel = null,
    string? Message = null
);

public class ChannelLinkUpdateHandler : IMessageHandler
{
    public MessageType Type => MessageType.ChannelLinkUpdate;

    private readonly IChannelService _channelService;
    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;

    public ChannelLinkUpdateHandler(
        IChannelService channelService,
        SessionManager sessionManager,
        IMessageSender messageSender)
    {
        _channelService = channelService;
        _sessionManager = sessionManager;
        _messageSender = messageSender;
    }

    public async ValueTask HandleAsync(ConnectionContext context, Message message)
    {
        var session = _sessionManager.GetAuthenticatedSession(context.ConnectionId);
        if (session == null)
        {
            await SendAsync(context, message.SequenceId, new ChannelLinkResponse(false, Message: "Not authenticated"));
            return;
        }

        var request = PayloadSerializer.Deserialize<ChannelLinkUpdateRequest>(message.Payload);
        if (request == null || request.ChannelId == 0)
        {
            await SendAsync(context, message.SequenceId, new ChannelLinkResponse(false, Message: "Invalid payload"));
            return;
        }

        try
        {
            var channel = await _channelService.UpdateChannelLinksAsync(
                request.ChannelId,
                session.UserId,
                request.PublicAlias,
                request.RegeneratePrivateInvite);

            var inviteLink = await _channelService.GetInviteLinkAsync(channel.Id, session.UserId);
            var publicLink = await _channelService.GetPublicLinkAsync(channel.Id);
            var link = new ChannelLinkInfo(channel.Id, channel.PublicAlias, publicLink, inviteLink);

            await SendAsync(context, message.SequenceId, new ChannelLinkResponse(true, link));
        }
        catch (Exception ex)
        {
            await SendAsync(context, message.SequenceId, new ChannelLinkResponse(false, Message: ex.Message));
        }
    }

    private Task SendAsync(ConnectionContext context, ulong sequenceId, ChannelLinkResponse response)
    {
        return _messageSender.SendProtocolMessageAsync(
            context.ConnectionId,
            (ushort)MessageType.ChannelLinkUpdateResponse,
            sequenceId,
            PayloadSerializer.Serialize(response));
    }
}

public class ChannelLinkGetHandler : IMessageHandler
{
    public MessageType Type => MessageType.ChannelLinkGet;

    private readonly IChannelService _channelService;
    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;

    public ChannelLinkGetHandler(
        IChannelService channelService,
        SessionManager sessionManager,
        IMessageSender messageSender)
    {
        _channelService = channelService;
        _sessionManager = sessionManager;
        _messageSender = messageSender;
    }

    public async ValueTask HandleAsync(ConnectionContext context, Message message)
    {
        var session = _sessionManager.GetAuthenticatedSession(context.ConnectionId);
        if (session == null)
        {
            await SendAsync(context, message.SequenceId, new ChannelLinkResponse(false, Message: "Not authenticated"));
            return;
        }

        var request = PayloadSerializer.Deserialize<ChannelLinkRequest>(message.Payload);
        if (request == null || request.ChannelId == 0)
        {
            await SendAsync(context, message.SequenceId, new ChannelLinkResponse(false, Message: "Invalid payload"));
            return;
        }

        try
        {
            var inviteLink = await _channelService.GetInviteLinkAsync(request.ChannelId, session.UserId);
            var publicLink = await _channelService.GetPublicLinkAsync(request.ChannelId);
            var resolved = await _channelService.ResolveByLinkAsync(publicLink ?? inviteLink);
            var link = new ChannelLinkInfo(request.ChannelId, resolved?.PublicAlias, publicLink, inviteLink);

            await SendAsync(context, message.SequenceId, new ChannelLinkResponse(true, link));
        }
        catch (Exception ex)
        {
            await SendAsync(context, message.SequenceId, new ChannelLinkResponse(false, Message: ex.Message));
        }
    }

    private Task SendAsync(ConnectionContext context, ulong sequenceId, ChannelLinkResponse response)
    {
        return _messageSender.SendProtocolMessageAsync(
            context.ConnectionId,
            (ushort)MessageType.ChannelLinkGetResponse,
            sequenceId,
            PayloadSerializer.Serialize(response));
    }
}

public class ChannelResolveHandler : IMessageHandler
{
    public MessageType Type => MessageType.ChannelResolve;

    private readonly IChannelService _channelService;
    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;

    public ChannelResolveHandler(
        IChannelService channelService,
        SessionManager sessionManager,
        IMessageSender messageSender)
    {
        _channelService = channelService;
        _sessionManager = sessionManager;
        _messageSender = messageSender;
    }

    public async ValueTask HandleAsync(ConnectionContext context, Message message)
    {
        var session = _sessionManager.GetAuthenticatedSession(context.ConnectionId);
        if (session == null)
        {
            await SendAsync(context, message.SequenceId, new ChannelResolveResponse(false, Message: "Not authenticated"));
            return;
        }

        var request = PayloadSerializer.Deserialize<ChannelResolveRequest>(message.Payload);
        if (request == null || string.IsNullOrWhiteSpace(request.LinkOrAlias))
        {
            await SendAsync(context, message.SequenceId, new ChannelResolveResponse(false, Message: "Invalid payload"));
            return;
        }

        var channel = await _channelService.ResolveByLinkAsync(request.LinkOrAlias);
        if (channel == null)
        {
            await SendAsync(context, message.SequenceId, new ChannelResolveResponse(false, Message: "Channel not found"));
            return;
        }

        var summary = new ChannelSummary(channel.Id, channel.Name, channel.Description, (int)channel.Type, channel.MemberCount);
        await SendAsync(context, message.SequenceId, new ChannelResolveResponse(true, summary));
    }

    private Task SendAsync(ConnectionContext context, ulong sequenceId, ChannelResolveResponse response)
    {
        return _messageSender.SendProtocolMessageAsync(
            context.ConnectionId,
            (ushort)MessageType.ChannelResolveResponse,
            sequenceId,
            PayloadSerializer.Serialize(response));
    }
}

public class ChannelJoinByLinkHandler : IMessageHandler
{
    public MessageType Type => MessageType.ChannelJoinByLink;

    private readonly IChannelService _channelService;
    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;

    public ChannelJoinByLinkHandler(
        IChannelService channelService,
        SessionManager sessionManager,
        IMessageSender messageSender)
    {
        _channelService = channelService;
        _sessionManager = sessionManager;
        _messageSender = messageSender;
    }

    public async ValueTask HandleAsync(ConnectionContext context, Message message)
    {
        var session = _sessionManager.GetAuthenticatedSession(context.ConnectionId);
        if (session == null)
        {
            await SendAsync(context, message.SequenceId, new ChannelJoinResponse(false, Message: "Not authenticated"));
            return;
        }

        var request = PayloadSerializer.Deserialize<ChannelResolveRequest>(message.Payload);
        if (request == null || string.IsNullOrWhiteSpace(request.LinkOrAlias))
        {
            await SendAsync(context, message.SequenceId, new ChannelJoinResponse(false, Message: "Invalid payload"));
            return;
        }

        var channel = await _channelService.ResolveByLinkAsync(request.LinkOrAlias);
        if (channel == null)
        {
            await SendAsync(context, message.SequenceId, new ChannelJoinResponse(false, Message: "Channel not found"));
            return;
        }

        var result = channel.Type == Aegis.Data.Entities.ChannelType.Private
            ? await _channelService.JoinChannelByInviteCodeAsync(session.UserId, channel.InviteCode ?? string.Empty)
            : await _channelService.JoinChannelAsync(session.UserId, channel.Id);

        var summary = new ChannelSummary(
            result.Channel.Id,
            result.Channel.Name,
            result.Channel.Description,
            (int)result.Channel.Type,
            result.Channel.MemberCount);

        await SendAsync(
            context,
            message.SequenceId,
            new ChannelJoinResponse(true, summary, result.WasAlreadyMember ? "Already a member" : "Joined channel"));
    }

    private Task SendAsync(ConnectionContext context, ulong sequenceId, ChannelJoinResponse response)
    {
        return _messageSender.SendProtocolMessageAsync(
            context.ConnectionId,
            (ushort)MessageType.ChannelJoinByLinkResponse,
            sequenceId,
            PayloadSerializer.Serialize(response));
    }
}
