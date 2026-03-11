using Aegis.Protocol;
using Aegis.Transport;
using Aegis.Common;
using Microsoft.Extensions.Logging;

namespace Aegis.Handlers;

internal static class HandlerLoggingExtensions
{
    public static void LogHandlerError(
        this ILogger logger,
        Exception exception,
        string operation,
        ConnectionContext context,
        Message message,
        SessionManager? sessionManager = null)
    {
        var userId = sessionManager?.GetAuthenticatedSession(context.ConnectionId)?.UserId;

        logger.LogError(
            exception,
            "handler_error op={Operation} msgType={MessageType} seq={SequenceId} conn={ConnectionId} user={UserId} payloadBytes={PayloadBytes}",
            operation,
            message.Type,
            message.SequenceId,
            context.ConnectionId,
            userId,
            message.PayloadLength);
    }

    public static void LogHandlerError(
        this ILogger logger,
        Exception exception,
        string operation,
        ConnectionContext context,
        ulong? sequenceId = null,
        ulong? userId = null)
    {
        logger.LogError(
            exception,
            "handler_error op={Operation} seq={SequenceId} conn={ConnectionId} user={UserId}",
            operation,
            sequenceId,
            context.ConnectionId,
            userId);
    }
}
