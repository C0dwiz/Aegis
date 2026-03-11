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
        if (!IsTokenFormatValid(token))
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

    private static bool IsTokenFormatValid(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var separatorIndex = token.IndexOf(':');
        if (separatorIndex <= 0 || separatorIndex != token.LastIndexOf(':') || separatorIndex == token.Length - 1)
        {
            return false;
        }

        var prefix = token.AsSpan(0, separatorIndex);
        var secret = token.AsSpan(separatorIndex + 1);

        if (prefix.Length is < 3 or > 64 || secret.Length is < 20 or > 128)
        {
            return false;
        }

        var hasLetterInPrefix = false;
        foreach (var ch in prefix)
        {
            if (char.IsLetter(ch))
            {
                hasLetterInPrefix = true;
            }

            if (!char.IsLetterOrDigit(ch) && ch != '_' && ch != '-' && ch != '.')
            {
                return false;
            }
        }

        if (!hasLetterInPrefix)
        {
            return false;
        }

        foreach (var ch in secret)
        {
            if (!char.IsLetterOrDigit(ch) && ch != '_' && ch != '-')
            {
                return false;
            }
        }

        return true;
    }
}
