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

    [Fact]
    public void SendPhotoRequest_UsesTelegramLikeJsonFields()
    {
        var request = new SendPhotoRequest(
            ChatId: "u:7",
            PhotoBase64: "ZmFrZQ==",
            Caption: "pic",
            FileName: "photo.jpg",
            MimeType: "image/jpeg");

        var json = JsonSerializer.Serialize(request);

        Assert.Contains("\"chat_id\"", json);
        Assert.Contains("\"photo_base64\"", json);
        Assert.Contains("\"caption\"", json);
        Assert.Contains("\"file_name\"", json);
        Assert.Contains("\"mime_type\"", json);
    }

    [Fact]
    public void SendDocumentRequest_UsesTelegramLikeJsonFields()
    {
        var request = new SendDocumentRequest(
            ChatId: "u:8",
            FileBase64: "cGRm",
            Caption: "doc",
            FileName: "report.pdf",
            MimeType: "application/pdf");

        var json = JsonSerializer.Serialize(request);

        Assert.Contains("\"chat_id\"", json);
        Assert.Contains("\"file_base64\"", json);
        Assert.Contains("\"caption\"", json);
        Assert.Contains("\"file_name\"", json);
        Assert.Contains("\"mime_type\"", json);
    }
}
