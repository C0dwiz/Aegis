namespace Aegis.BotApi.Domain;

public enum ChatKind
{
    Private,
    Channel
}

public sealed record ChatResolveResult(bool IsValid, ChatKind Kind, ulong EntityId, string? Error)
{
    public static ChatResolveResult Private(ulong userId) => new(true, ChatKind.Private, userId, null);
    public static ChatResolveResult Channel(ulong channelId) => new(true, ChatKind.Channel, channelId, null);
    public static ChatResolveResult Invalid(string error) => new(false, default, 0, error);
}
