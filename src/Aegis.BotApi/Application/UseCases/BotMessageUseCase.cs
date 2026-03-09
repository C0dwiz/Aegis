using Aegis.BotApi.Application.Abstractions;
using Aegis.BotApi.Application.Models;
using Aegis.BotApi.Domain;
using Aegis.BotApi.Infrastructure.Auth;
using Aegis.Data.Services;

namespace Aegis.BotApi.Application.UseCases;

internal sealed class BotMessageUseCase : IBotMessageUseCase
{
    private readonly IBotAuthenticator _authenticator;
    private readonly IMessageService _messageService;
    private readonly IUserSearchService _userSearchService;

    public BotMessageUseCase(
        IBotAuthenticator authenticator,
        IMessageService messageService,
        IUserSearchService userSearchService)
    {
        _authenticator = authenticator;
        _messageService = messageService;
        _userSearchService = userSearchService;
    }

    public async Task<UseCaseResponse<BotIdentity>> GetMeAsync(string token)
    {
        var bot = await _authenticator.AuthenticateAsync(token);
        if (bot == null)
        {
            return UseCaseResponse<BotIdentity>.Fail(BotErrorCode.Unauthorized, "Unauthorized");
        }

        return UseCaseResponse<BotIdentity>.Ok(bot);
    }

    public async Task<UseCaseResponse<MessageView>> SendMessageAsync(string token, SendMessageCommand command)
    {
        var bot = await _authenticator.AuthenticateAsync(token);
        if (bot == null)
        {
            return UseCaseResponse<MessageView>.Fail(BotErrorCode.Unauthorized, "Unauthorized");
        }

        if (!command.Chat.IsValid)
        {
            return UseCaseResponse<MessageView>.Fail(BotErrorCode.Validation, command.Chat.Error ?? "Invalid chat");
        }

        if (command.Chat.Kind == ChatKind.Private)
        {
            var targetUser = await _userSearchService.FindUserByIdAsync(command.Chat.EntityId);
            if (targetUser == null)
            {
                return UseCaseResponse<MessageView>.Fail(BotErrorCode.NotFound, "Target user not found");
            }

            var message = await _messageService.SendPrivateMessageAsync(
                bot.UserId,
                command.Chat.EntityId,
                command.Content,
                command.ContentType);

            return UseCaseResponse<MessageView>.Ok(new MessageView(
                message.Id,
                command.Chat.Kind,
                command.Chat.EntityId,
                message.Content,
                message.ContentType,
                message.CreatedAt));
        }

        var channelMessage = await _messageService.SendChannelMessageAsync(
            command.Chat.EntityId,
            bot.UserId,
            command.Content,
            command.ContentType);

        return UseCaseResponse<MessageView>.Ok(new MessageView(
            channelMessage.Id,
            command.Chat.Kind,
            command.Chat.EntityId,
            channelMessage.Content,
            channelMessage.ContentType,
            channelMessage.CreatedAt));
    }

    public async Task<UseCaseResponse<MessageView>> EditMessageTextAsync(string token, EditMessageCommand command)
    {
        var bot = await _authenticator.AuthenticateAsync(token);
        if (bot == null)
        {
            return UseCaseResponse<MessageView>.Fail(BotErrorCode.Unauthorized, "Unauthorized");
        }

        if (!command.Chat.IsValid)
        {
            return UseCaseResponse<MessageView>.Fail(BotErrorCode.Validation, command.Chat.Error ?? "Invalid chat");
        }

        try
        {
            if (command.Chat.Kind == ChatKind.Channel)
            {
                var updated = await _messageService.EditChannelMessageAsync(
                    command.MessageId,
                    bot.UserId,
                    command.Chat.EntityId,
                    command.Content);

                return UseCaseResponse<MessageView>.Ok(new MessageView(
                    updated.Id,
                    command.Chat.Kind,
                    command.Chat.EntityId,
                    updated.Content,
                    updated.ContentType,
                    updated.CreatedAt));
            }

            var edited = await _messageService.EditMessageAsync(command.MessageId, bot.UserId, command.Content);
            return UseCaseResponse<MessageView>.Ok(new MessageView(
                edited.Id,
                command.Chat.Kind,
                command.Chat.EntityId,
                edited.Content,
                edited.ContentType,
                edited.CreatedAt));
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            return UseCaseResponse<MessageView>.Fail(BotErrorCode.Failed, ex.Message);
        }
    }

    public async Task<UseCaseResponse<bool>> DeleteMessageAsync(string token, DeleteMessageCommand command)
    {
        var bot = await _authenticator.AuthenticateAsync(token);
        if (bot == null)
        {
            return UseCaseResponse<bool>.Fail(BotErrorCode.Unauthorized, "Unauthorized");
        }

        if (!command.Chat.IsValid)
        {
            return UseCaseResponse<bool>.Fail(BotErrorCode.Validation, command.Chat.Error ?? "Invalid chat");
        }

        var deleted = command.Chat.Kind == ChatKind.Channel
            ? await _messageService.DeleteChannelMessageAsync(command.MessageId, bot.UserId, command.Chat.EntityId)
            : await _messageService.DeleteMessageAsync(command.MessageId, bot.UserId);

        return deleted
            ? UseCaseResponse<bool>.Ok(true)
            : UseCaseResponse<bool>.Fail(BotErrorCode.Failed, "Message was not deleted");
    }
}
