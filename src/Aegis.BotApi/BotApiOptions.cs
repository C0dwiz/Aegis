namespace Aegis.BotApi;

public sealed class BotApiOptions
{
    public const string SectionName = "BotApi";

    public List<BotDefinition> Bots { get; set; } = new();
}

public sealed class BotDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public ulong UserId { get; set; }
}
