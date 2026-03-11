using System.Text.Json;
using Aegis.BotApi.Contracts;

namespace Aegis.BotApi.Services;

public sealed class RichContentFormatter
{
    private static readonly HashSet<string> AllowedParseModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "markdown",
        "markdownv2",
        "html"
    };

    private sealed record RichContentEnvelope(
        string Kind,
        string Text,
        string? ParseMode,
        ReplyMarkupRequest? ReplyMarkup);

    public string BuildContent(string text, string? parseMode, ReplyMarkupRequest? replyMarkup)
    {
        var normalizedParseMode = NormalizeParseMode(parseMode);

        if (replyMarkup == null && normalizedParseMode == null)
        {
            return text;
        }

        var envelope = new RichContentEnvelope("bot-rich-text", text, normalizedParseMode, replyMarkup);
        return JsonSerializer.Serialize(envelope);
    }

    private static string? NormalizeParseMode(string? parseMode)
    {
        if (string.IsNullOrWhiteSpace(parseMode))
        {
            return null;
        }

        var normalized = parseMode.Trim().ToLowerInvariant();
        return AllowedParseModes.Contains(normalized) ? normalized : null;
    }
}
