using Aegis.BotApi.Application.Models;
using Aegis.BotApi.Domain;

namespace Aegis.BotApi.Application.Abstractions;

public interface IBotMessageUseCase
{
    Task<UseCaseResponse<BotIdentity>> GetMeAsync(string token);
    Task<UseCaseResponse<MessageView>> SendMessageAsync(string token, SendMessageCommand command);
    Task<UseCaseResponse<MessageView>> EditMessageTextAsync(string token, EditMessageCommand command);
    Task<UseCaseResponse<bool>> DeleteMessageAsync(string token, DeleteMessageCommand command);
}
