using System.Text.Json;
using Aegis.BotApi.Contracts;

namespace Aegis.BotApi.Services;

public sealed class RichContentFormatter
{
    private sealed record RichContentEnvelope(
        string Kind,
        string Text,
        string? ParseMode,
        ReplyMarkupRequest? ReplyMarkup);

    public string BuildContent(string text, string? parseMode, ReplyMarkupRequest? replyMarkup)
    {
        if (replyMarkup == null && string.IsNullOrWhiteSpace(parseMode))
        {
            return text;
        }

        var envelope = new RichContentEnvelope("bot-rich-text", text, parseMode, replyMarkup);
        return JsonSerializer.Serialize(envelope);
    }
}
