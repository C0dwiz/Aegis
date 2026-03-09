using Aegis.BotApi.Application.Abstractions;
using Aegis.BotApi.Contracts;
using Aegis.BotApi.Mappers;

namespace Aegis.BotApi.Endpoints;

public static class BotEndpoints
{
    public static IEndpointRouteBuilder MapBotEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/bot/{token}/getMe", GetMe)
            .WithName("GetMe");

        app.MapPost("/bot/{token}/sendMessage", SendMessage)
            .WithName("SendMessage");

        app.MapPost("/bot/{token}/editMessageText", EditMessageText)
            .WithName("EditMessageText");

        app.MapPost("/bot/{token}/deleteMessage", DeleteMessage)
            .WithName("DeleteMessage");

        return app;
    }

    private static async Task<IResult> GetMe(string token, IBotMessageUseCase useCase)
    {
        var response = await useCase.GetMeAsync(token);
        return BotResponseMapper.ToHttpResult(response);
    }

    private static async Task<IResult> SendMessage(
        string token,
        SendMessageRequest request,
        BotRequestMapper requestMapper,
        IBotMessageUseCase useCase)
    {
        var command = requestMapper.Map(request);
        var response = await useCase.SendMessageAsync(token, command);
        return BotResponseMapper.ToHttpResult(response);
    }

    private static async Task<IResult> EditMessageText(
        string token,
        EditMessageTextRequest request,
        BotRequestMapper requestMapper,
        IBotMessageUseCase useCase)
    {
        var command = requestMapper.Map(request);
        var response = await useCase.EditMessageTextAsync(token, command);
        return BotResponseMapper.ToHttpResult(response);
    }

    private static async Task<IResult> DeleteMessage(
        string token,
        DeleteMessageRequest request,
        BotRequestMapper requestMapper,
        IBotMessageUseCase useCase)
    {
        var command = requestMapper.Map(request);
        var response = await useCase.DeleteMessageAsync(token, command);
        return BotResponseMapper.ToHttpResult(response);
    }
}
