using Aegis.BotApi.Contracts;
using Aegis.BotApi.Domain;
using Aegis.BotApi.Services;

namespace Aegis.BotApi.Mappers;

internal sealed class BotRequestMapper
{
    private readonly ChatIdResolver _chatIdResolver;
    private readonly RichContentFormatter _contentFormatter;

    public BotRequestMapper(ChatIdResolver chatIdResolver, RichContentFormatter contentFormatter)
    {
        _chatIdResolver = chatIdResolver;
        _contentFormatter = contentFormatter;
    }

    public SendMessageCommand Map(SendMessageRequest request)
    {
        var resolved = _chatIdResolver.Resolve(request.ChatId);
        var content = _contentFormatter.BuildContent(request.Text, request.ParseMode, request.ReplyMarkup);
        return new SendMessageCommand(resolved, content, request.ContentType);
    }

    public EditMessageCommand Map(EditMessageTextRequest request)
    {
        var resolved = _chatIdResolver.Resolve(request.ChatId);
        var content = _contentFormatter.BuildContent(request.Text, request.ParseMode, request.ReplyMarkup);
        return new EditMessageCommand(resolved, request.MessageId, content, request.ContentType);
    }

    public DeleteMessageCommand Map(DeleteMessageRequest request)
    {
        var resolved = _chatIdResolver.Resolve(request.ChatId);
        return new DeleteMessageCommand(resolved, request.MessageId);
    }
}
