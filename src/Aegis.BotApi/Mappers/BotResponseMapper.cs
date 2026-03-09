using Aegis.BotApi.Application.Models;
using Aegis.BotApi.Contracts;

namespace Aegis.BotApi.Mappers;

internal static class BotResponseMapper
{
    public static IResult ToHttpResult(UseCaseResponse<BotIdentity> response)
    {
        if (!response.Success)
        {
            return ErrorResult(response.ErrorCode, TelegramLikeResponse<GetMeResult>.Failure(response.ErrorMessage ?? "Request failed"));
        }

        var me = response.Result!;
        return Results.Ok(TelegramLikeResponse<GetMeResult>.Success(new GetMeResult(me.Name, me.UserId, me.IsBot)));
    }

    public static IResult ToHttpResult(UseCaseResponse<MessageView> response)
    {
        if (!response.Success)
        {
            return ErrorResult(response.ErrorCode, TelegramLikeResponse<MessageResult>.Failure(response.ErrorMessage ?? "Request failed"));
        }

        var message = response.Result!;
        var chatId = message.ChatKind == Domain.ChatKind.Private ? $"u:{message.ChatEntityId}" : $"c:{message.ChatEntityId}";
        var result = new MessageResult(message.MessageId, chatId, message.Text, message.ContentType, message.Date);
        return Results.Ok(TelegramLikeResponse<MessageResult>.Success(result));
    }

    public static IResult ToHttpResult(UseCaseResponse<bool> response)
    {
        if (!response.Success)
        {
            return ErrorResult(response.ErrorCode, TelegramLikeResponse<bool>.Failure(response.ErrorMessage ?? "Request failed"));
        }

        return Results.Ok(TelegramLikeResponse<bool>.Success(true));
    }

    private static IResult ErrorResult<T>(BotErrorCode? code, TelegramLikeResponse<T> payload)
    {
        return code switch
        {
            BotErrorCode.Unauthorized => Results.Unauthorized(),
            BotErrorCode.NotFound => Results.NotFound(payload),
            BotErrorCode.Validation => Results.BadRequest(payload),
            _ => Results.BadRequest(payload)
        };
    }
}
