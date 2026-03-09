using System.Text.Json;
using Aegis.BotApi;
using Aegis.BotApi.Contracts;
using Xunit;

namespace Aegis.Tests;

public class BotApiContractsTests
{
    [Fact]
    public void BotApiOptions_HasExpectedSectionName()
    {
        Assert.Equal("BotApi", BotApiOptions.SectionName);
    }

    [Fact]
    public void SendMessageRequest_UsesTelegramLikeJsonFields()
    {
        var request = new SendMessageRequest(
            ChatId: "u:42",
            Text: "hello",
            ParseMode: "Markdown",
            ReplyMarkup: new ReplyMarkupRequest(new List<List<InlineKeyboardButtonRequest>>
            {
                new()
                {
                    new InlineKeyboardButtonRequest("Open", "open", "https://example.com")
                }
            }));

        var json = JsonSerializer.Serialize(request);

        Assert.Contains("\"chat_id\"", json);
        Assert.Contains("\"text\"", json);
        Assert.Contains("\"parse_mode\"", json);
        Assert.Contains("\"reply_markup\"", json);
        Assert.Contains("\"inline_keyboard\"", json);
    }

    [Fact]
    public void MessageResult_UsesTelegramLikeJsonFields()
    {
        var result = new MessageResult(
            MessageId: 15,
            ChatId: "c:7",
            Text: "payload",
            ContentType: Aegis.Data.Entities.MessageContentType.Text,
            Date: DateTime.UtcNow);

        var json = JsonSerializer.Serialize(result);

        Assert.Contains("\"message_id\"", json);
        Assert.Contains("\"chat_id\"", json);
        Assert.Contains("\"content_type\"", json);
        Assert.Contains("\"date\"", json);
    }
}
