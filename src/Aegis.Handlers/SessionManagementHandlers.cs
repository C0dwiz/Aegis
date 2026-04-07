using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Aegis.Common;
using Aegis.Data.Repositories;
using Aegis.Protocol;
using Aegis.Transport;
using ProtocolMessage = Aegis.Protocol.Message;

namespace Aegis.Handlers;

// ===================== SESSION LIST HANDLER =====================

/// <summary>
/// Returns all active sessions (devices) for the authenticated user.
/// Client sends SessionListRequest; server replies with SessionListResponse.
/// </summary>
public class SessionListHandler : IMessageHandler
{
    public MessageType Type => MessageType.SessionListRequest;

    private readonly ILogger<SessionListHandler> _logger;
    private readonly IServiceProvider _serviceProvider;

    public SessionListHandler(ILogger<SessionListHandler> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public async ValueTask HandleAsync(ConnectionContext context, ProtocolMessage message)
    {
        using var scope = _serviceProvider.CreateScope();
        var sessionManager = scope.ServiceProvider.GetRequiredService<SessionManager>();
        var sessionRepository = scope.ServiceProvider.GetRequiredService<ISessionRepository>();
        var messageSender = scope.ServiceProvider.GetRequiredService<IMessageSender>();

        var session = sessionManager.GetAuthenticatedSession(context.ConnectionId);
        if (session == null)
        {
            await SendResponseAsync(messageSender, context, message.SequenceId, false, "Not authenticated", null);
            return;
        }

        try
        {
            var sessions = await sessionRepository.GetUserActiveSessions(session.UserId);
            var activeConnectionIds = sessionManager.GetConnectionIdsByUserId(session.UserId);

            var items = sessions.Select(s => new SessionListItem(
                SessionId: s.Id,
                ClientInfo: s.ClientInfo,
                IpAddress: s.IpAddress,
                CreatedAt: s.CreatedAt,
                LastActivityAt: s.LastActivityAt,
                IsCurrent: s.ConnectionId == context.ConnectionId.ToString(),
                IsOnline: activeConnectionIds.Any(c => c.ToString() == s.ConnectionId)
            )).ToArray();

            await SendResponseAsync(messageSender, context, message.SequenceId, true, null, items);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing sessions for user {UserId}", session.UserId);
            await SendResponseAsync(messageSender, context, message.SequenceId, false, "Internal error", null);
        }
    }

    private static async Task SendResponseAsync(
        IMessageSender sender, ConnectionContext context, ulong seqId,
        bool success, string? error, SessionListItem[]? sessions)
    {
        var payload = PayloadSerializer.Serialize(new SessionListResponse(success, error, sessions ?? []));
        await sender.SendProtocolMessageAsync(
            context.ConnectionId,
            (ushort)MessageType.SessionListResponse,
            seqId,
            payload);
    }
}

// ===================== SESSION REVOKE HANDLER =====================

/// <summary>
/// Allows an authenticated user to terminate another one of their own sessions by sessionId.
/// The revoked session receives a <see cref="MessageType.SessionTerminatedEvent"/> before being disconnected.
/// </summary>
public class SessionRevokeHandler : IMessageHandler
{
    public MessageType Type => MessageType.SessionRevokeRequest;

    private readonly ILogger<SessionRevokeHandler> _logger;
    private readonly IServiceProvider _serviceProvider;

    public SessionRevokeHandler(ILogger<SessionRevokeHandler> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public async ValueTask HandleAsync(ConnectionContext context, ProtocolMessage message)
    {
        using var scope = _serviceProvider.CreateScope();
        var sessionManager = scope.ServiceProvider.GetRequiredService<SessionManager>();
        var sessionRepository = scope.ServiceProvider.GetRequiredService<ISessionRepository>();
        var messageSender = scope.ServiceProvider.GetRequiredService<IMessageSender>();

        var callerSession = sessionManager.GetAuthenticatedSession(context.ConnectionId);
        if (callerSession == null)
        {
            await SendResponseAsync(messageSender, context, message.SequenceId, false, "Not authenticated");
            return;
        }

        try
        {
            var req = PayloadSerializer.Deserialize<SessionRevokeRequest>(message.Payload);
            if (req == null || req.SessionId == 0)
            {
                await SendResponseAsync(messageSender, context, message.SequenceId, false, "Invalid request");
                return;
            }

            // Prevent revoking own current session via this endpoint; use logout for that.
            if (req.SessionId == callerSession.ConnectionId)
            {
                await SendResponseAsync(messageSender, context, message.SequenceId, false,
                    "Cannot revoke your current session; use logout instead");
                return;
            }

            // Find the in-memory connection for the target session so we can push a disconnect event.
            var allConnIds = sessionManager.GetConnectionIdsByUserId(callerSession.UserId);
            ulong? targetConnId = null;
            foreach (var connId in allConnIds)
            {
                var s = sessionManager.GetSession(connId);
                if (s != null && s.ConnectionId == req.SessionId) { targetConnId = connId; break; }
            }

            // Deactivate in DB (ownership check is inside).
            var deactivated = await sessionRepository.DeactivateSessionAsync(req.SessionId, callerSession.UserId);
            if (!deactivated)
            {
                await SendResponseAsync(messageSender, context, message.SequenceId, false,
                    "Session not found or already inactive");
                return;
            }

            // Push termination event to target device if it is currently online.
            if (targetConnId.HasValue)
            {
                var terminatedPayload = PayloadSerializer.Serialize(new SessionTerminatedEvent(
                    Reason: "revoked_by_user",
                    RevokedByConnectionId: context.ConnectionId));
                try
                {
                    await messageSender.SendProtocolMessageAsync(
                        targetConnId.Value,
                        (ushort)MessageType.SessionTerminatedEvent,
                        0,
                        terminatedPayload,
                        allowUnsigned: false);
                }
                catch { /* best-effort; the connection may already be closing */ }

                sessionManager.RemoveSession(targetConnId.Value);
            }

            _logger.LogInformation("User {UserId} revoked session {SessionId}", callerSession.UserId, req.SessionId);
            await SendResponseAsync(messageSender, context, message.SequenceId, true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revoking session for user {UserId}", callerSession.UserId);
            await SendResponseAsync(messageSender, context, message.SequenceId, false, "Internal error");
        }
    }

    private static async Task SendResponseAsync(
        IMessageSender sender, ConnectionContext context, ulong seqId, bool success, string? error)
    {
        var payload = PayloadSerializer.Serialize(new SessionRevokeResponse(success, error));
        await sender.SendProtocolMessageAsync(
            context.ConnectionId,
            (ushort)MessageType.SessionRevokeResponse,
            seqId,
            payload);
    }
}

// ===================== PROTOCOL RECORDS =====================

public record SessionListItem(
    ulong SessionId,
    string ClientInfo,
    string? IpAddress,
    DateTime CreatedAt,
    DateTime? LastActivityAt,
    bool IsCurrent,
    bool IsOnline);

public record SessionListResponse(
    bool Success,
    string? Error,
    SessionListItem[] Sessions);

public record SessionRevokeRequest(ulong SessionId);

public record SessionRevokeResponse(bool Success, string? Error);

public record SessionTerminatedEvent(string Reason, ulong RevokedByConnectionId);
