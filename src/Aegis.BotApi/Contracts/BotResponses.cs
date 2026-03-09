using System.Text.Json.Serialization;
using Aegis.Data.Entities;

namespace Aegis.BotApi.Contracts;

public sealed record GetMeResult(string Name, ulong UserId, bool IsBot);

public sealed record TelegramLikeResponse<T>(bool Ok, T? Result = default, string? Description = null)
{
    public static TelegramLikeResponse<T> Success(T result) => new(true, result, null);
    public static TelegramLikeResponse<T> Failure(string description) => new(false, default, description);
}

public sealed record MessageResult(
    [property: JsonPropertyName("message_id")] ulong MessageId,
    [property: JsonPropertyName("chat_id")] string ChatId,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("content_type")] MessageContentType ContentType,
    [property: JsonPropertyName("date")] DateTime Date);
