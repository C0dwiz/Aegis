using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Aegis.Common;
using Aegis.Data.Entities;
using Aegis.Data.Repositories;

namespace Aegis.Data.Services;

public interface IBotManagementService
{
    Task EnsureBotFatherExistsAsync();
    Task<bool> IsBotFatherAsync(ulong userId);
    Task<IReadOnlyList<string>> ProcessBotFatherMessageAsync(ulong ownerUserId, string text);
    Task<(ulong BotUserId, string BotName)?> ValidateBotTokenAsync(string rawToken);
}

public class BotManagementService : IBotManagementService
{
    private const string BotFatherUsername = "BotFather";
    private const string BotFatherEmail = "botfather@system.local";
    private const string BotFatherPublicKey = "BOTFATHER_SYSTEM_KEY";

    private static readonly Regex BotUsernameRegex = new(@"^[a-zA-Z0-9][a-zA-Z0-9_.-]{2,31}bot$", RegexOptions.Compiled);

    private readonly IUserRepository _userRepository;
    private readonly IBotRepository _botRepository;
    private readonly IBotTokenRepository _botTokenRepository;
    private readonly IBotConversationStateRepository _stateRepository;
    private readonly ICryptoProvider _cryptoProvider;

    public BotManagementService(
        IUserRepository userRepository,
        IBotRepository botRepository,
        IBotTokenRepository botTokenRepository,
        IBotConversationStateRepository stateRepository,
        ICryptoProvider cryptoProvider)
    {
        _userRepository = userRepository;
        _botRepository = botRepository;
        _botTokenRepository = botTokenRepository;
        _stateRepository = stateRepository;
        _cryptoProvider = cryptoProvider;
    }

    public async Task EnsureBotFatherExistsAsync()
    {
        var existing = await _userRepository.GetByUsernameAsync(BotFatherUsername);
        if (existing != null)
        {
            return;
        }

        var passwordHash = await _cryptoProvider.HashPasswordAsync(Guid.NewGuid().ToString("N"));
        await _userRepository.CreateAsync(new User
        {
            Username = BotFatherUsername,
            Email = BotFatherEmail,
            PasswordHash = passwordHash,
            PublicKey = BotFatherPublicKey,
            DisplayName = "BotFather",
            Bio = "Official bot management account",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
    }

    public async Task<bool> IsBotFatherAsync(ulong userId)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        return user != null && string.Equals(user.Username, BotFatherUsername, StringComparison.Ordinal);
    }

    public async Task<IReadOnlyList<string>> ProcessBotFatherMessageAsync(ulong ownerUserId, string text)
    {
        var input = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            return ["Send /help to see available commands."];
        }

        if (string.Equals(input, "/help", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(input, "/start", StringComparison.OrdinalIgnoreCase))
        {
            await _stateRepository.ResetAsync(ownerUserId);
            return [
                "I can help you create and manage bots.",
                "Commands: /newbot, /mybots, /token, /revoke, /cancel"
            ];
        }

        if (string.Equals(input, "/cancel", StringComparison.OrdinalIgnoreCase))
        {
            await _stateRepository.ResetAsync(ownerUserId);
            return ["Cancelled. You can start again with /newbot."];
        }

        if (string.Equals(input, "/newbot", StringComparison.OrdinalIgnoreCase))
        {
            var state = await _stateRepository.GetOrCreateAsync(ownerUserId);
            state.Step = BotConversationStep.AwaitingDisplayName;
            state.DraftDisplayName = null;
            state.DraftUsername = null;
            await _stateRepository.UpdateAsync(state);
            return ["Alright. Send me the display name for your new bot."];
        }

        if (string.Equals(input, "/mybots", StringComparison.OrdinalIgnoreCase))
        {
            await _stateRepository.ResetAsync(ownerUserId);
            var bots = (await _botRepository.GetByOwnerUserIdAsync(ownerUserId)).ToList();
            if (bots.Count == 0)
            {
                return ["You do not have any bots yet. Use /newbot to create one."];
            }

            var lines = new List<string> { "Your bots:" };
            lines.AddRange(bots.Select(b => $"@{b.Username} (id: {b.UserId})"));
            return lines;
        }

        if (string.Equals(input, "/token", StringComparison.OrdinalIgnoreCase))
        {
            var state = await _stateRepository.GetOrCreateAsync(ownerUserId);
            state.Step = BotConversationStep.AwaitingTokenUsername;
            state.DraftDisplayName = null;
            state.DraftUsername = null;
            await _stateRepository.UpdateAsync(state);
            return ["Send bot username to generate a new token (example: myshopbot)."];
        }

        if (string.Equals(input, "/revoke", StringComparison.OrdinalIgnoreCase))
        {
            var state = await _stateRepository.GetOrCreateAsync(ownerUserId);
            state.Step = BotConversationStep.AwaitingRevokeUsername;
            state.DraftDisplayName = null;
            state.DraftUsername = null;
            await _stateRepository.UpdateAsync(state);
            return ["Send bot username to revoke all active tokens."];
        }

        var current = await _stateRepository.GetOrCreateAsync(ownerUserId);
        switch (current.Step)
        {
            case BotConversationStep.AwaitingDisplayName:
                if (input.Length < 2)
                {
                    return ["Display name is too short. Send at least 2 characters."];
                }

                current.DraftDisplayName = input;
                current.Step = BotConversationStep.AwaitingUsername;
                await _stateRepository.UpdateAsync(current);
                return ["Great. Now send a username ending with `bot` (example: myshopbot)."];

            case BotConversationStep.AwaitingUsername:
                {
                    var username = NormalizeUsername(input);
                    var validationError = await ValidateNewBotUsernameAsync(username);
                    if (validationError != null)
                    {
                        return [validationError];
                    }

                    var displayName = current.DraftDisplayName ?? "Aegis Bot";
                    var (bot, token) = await CreateBotAsync(ownerUserId, displayName, username);
                    await _stateRepository.ResetAsync(ownerUserId);

                    return [
                        $"Done. Bot @{bot.Username} created.",
                        "Use this token in Bot API Authorization header:",
                        token
                    ];
                }

            case BotConversationStep.AwaitingTokenUsername:
                {
                    var username = NormalizeUsername(input);
                    var bot = await _botRepository.GetByUsernameAsync(username);
                    if (bot == null || bot.OwnerUserId != ownerUserId)
                    {
                        return ["Bot not found or you are not the owner. Try again or /cancel."];
                    }

                    var token = await IssueTokenAsync(bot, revokeExisting: true);
                    await _stateRepository.ResetAsync(ownerUserId);

                    return [
                        $"New token for @{bot.Username}:",
                        token,
                        "Previous active tokens were revoked."
                    ];
                }

            case BotConversationStep.AwaitingRevokeUsername:
                {
                    var username = NormalizeUsername(input);
                    var bot = await _botRepository.GetByUsernameAsync(username);
                    if (bot == null || bot.OwnerUserId != ownerUserId)
                    {
                        return ["Bot not found or you are not the owner. Try again or /cancel."];
                    }

                    await _botTokenRepository.RevokeAllActiveByBotIdAsync(bot.Id);
                    await _stateRepository.ResetAsync(ownerUserId);
                    return [$"All active tokens for @{bot.Username} were revoked."];
                }

            default:
                return ["Unknown command. Send /help to see available commands."];
        }
    }

    public async Task<(ulong BotUserId, string BotName)?> ValidateBotTokenAsync(string rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return null;
        }

        var tokenHash = HashToken(rawToken);
        var token = await _botTokenRepository.GetActiveByTokenHashAsync(tokenHash);
        if (token?.Bot?.User == null)
        {
            return null;
        }

        token.LastUsedAt = DateTime.UtcNow;
        await _botTokenRepository.UpdateAsync(token);

        return (token.Bot.UserId, token.Bot.User.Username);
    }

    private async Task<string?> ValidateNewBotUsernameAsync(string username)
    {
        if (!BotUsernameRegex.IsMatch(username))
        {
            return "Username must be 3-32 chars, valid symbols, and end with `bot`.";
        }

        var existingUser = await _userRepository.GetByUsernameAsync(username);
        if (existingUser != null)
        {
            return "This username is already taken. Send another one.";
        }

        var existingBot = await _botRepository.GetByUsernameAsync(username);
        if (existingBot != null)
        {
            return "This bot username already exists. Send another one.";
        }

        return null;
    }

    private async Task<(Bot Bot, string Token)> CreateBotAsync(ulong ownerUserId, string displayName, string username)
    {
        var passwordHash = await _cryptoProvider.HashPasswordAsync(Guid.NewGuid().ToString("N"));

        var user = await _userRepository.CreateAsync(new User
        {
            Username = username,
            Email = $"{username}@bots.local",
            PasswordHash = passwordHash,
            PublicKey = $"BOT_KEY_{Guid.NewGuid():N}",
            DisplayName = displayName,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        var bot = await _botRepository.CreateAsync(new Bot
        {
            OwnerUserId = ownerUserId,
            UserId = user.Id,
            Username = username,
            DisplayName = displayName,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });

        var token = await IssueTokenAsync(bot, revokeExisting: false);
        return (bot, token);
    }

    private async Task<string> IssueTokenAsync(Bot bot, bool revokeExisting)
    {
        if (revokeExisting)
        {
            await _botTokenRepository.RevokeAllActiveByBotIdAsync(bot.Id);
        }

        var secret = GenerateTokenSecret();
        var rawToken = $"{bot.UserId}:{secret}";
        var hash = HashToken(rawToken);

        await _botTokenRepository.CreateAsync(new BotToken
        {
            BotId = bot.Id,
            TokenHash = hash,
            CreatedAt = DateTime.UtcNow
        });

        return rawToken;
    }

    private static string NormalizeUsername(string value)
    {
        var username = value.Trim();
        if (username.StartsWith('@'))
        {
            username = username[1..];
        }

        return username;
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string GenerateTokenSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
