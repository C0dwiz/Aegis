using Aegis.BotApi.Domain;
using Aegis.Data.Entities;

namespace Aegis.BotApi.Application.Models;

public enum BotErrorCode
{
    Unauthorized,
    Validation,
    NotFound,
    Failed
}

public sealed record UseCaseResponse<T>(
    bool Success,
    T? Result = default,
    BotErrorCode? ErrorCode = null,
    string? ErrorMessage = null)
{
    public static UseCaseResponse<T> Ok(T result) => new(true, result);
    public static UseCaseResponse<T> Fail(BotErrorCode code, string message) => new(false, default, code, message);
}

public sealed record BotIdentity(string Name, ulong UserId, bool IsBot = true);

public sealed record MessageView(
    ulong MessageId,
    ChatKind ChatKind,
    ulong ChatEntityId,
    string Text,
    MessageContentType ContentType,
    DateTime Date);
