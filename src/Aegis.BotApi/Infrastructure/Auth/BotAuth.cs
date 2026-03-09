using Aegis.BotApi.Application.Models;
using Aegis.Data.Services;

namespace Aegis.BotApi.Infrastructure.Auth;

public interface IBotAuthenticator
{
    Task<BotIdentity?> AuthenticateAsync(string token);
}

internal sealed class BotAuthenticator : IBotAuthenticator
{
    private readonly IBotManagementService _botManagementService;

    public BotAuthenticator(IBotManagementService botManagementService)
    {
        _botManagementService = botManagementService;
    }

    public async Task<BotIdentity?> AuthenticateAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var identity = await _botManagementService.ValidateBotTokenAsync(token);
        if (identity == null)
        {
            return null;
        }

        return new BotIdentity(identity.Value.BotName, identity.Value.BotUserId, true);
    }
}
