using System.Text.Json;
using Aegis.Common;
using Aegis.Transport;

namespace Aegis.Handlers;

internal static class HandlerResponseSender
{
    public static Task SendAsync<TResponse>(
        Aegis.Common.IMessageSender messageSender,
        ConnectionContext context,
        Aegis.Protocol.MessageType responseType,
        ulong sequenceId,
        TResponse response)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(response);
        return messageSender.SendProtocolMessageAsync(
            context.ConnectionId,
            (ushort)responseType,
            sequenceId,
            payload);
    }
}
