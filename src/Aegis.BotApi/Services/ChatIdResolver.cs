using Aegis.BotApi.Domain;

namespace Aegis.BotApi.Services;

internal sealed class ChatIdResolver
{
    public ChatResolveResult Resolve(string chatId)
    {
        if (string.IsNullOrWhiteSpace(chatId))
        {
            return ChatResolveResult.Invalid("chat_id is required");
        }

        if (chatId.StartsWith("u:", StringComparison.OrdinalIgnoreCase) &&
            ulong.TryParse(chatId[2..], out var userId) && userId > 0)
        {
            return ChatResolveResult.Private(userId);
        }

        if (chatId.StartsWith("c:", StringComparison.OrdinalIgnoreCase) &&
            ulong.TryParse(chatId[2..], out var channelId) && channelId > 0)
        {
            return ChatResolveResult.Channel(channelId);
        }

        return ChatResolveResult.Invalid("chat_id must be in format 'u:<userId>' or 'c:<channelId>'");
    }
}
