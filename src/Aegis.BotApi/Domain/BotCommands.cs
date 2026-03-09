using Aegis.Data.Entities;

namespace Aegis.BotApi.Domain;

public sealed record SendMessageCommand(
    ChatResolveResult Chat,
    string Content,
    MessageContentType ContentType = MessageContentType.Text);

public sealed record EditMessageCommand(
    ChatResolveResult Chat,
    ulong MessageId,
    string Content,
    MessageContentType ContentType = MessageContentType.Text);

public sealed record DeleteMessageCommand(
    ChatResolveResult Chat,
    ulong MessageId);
