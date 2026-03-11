using Aegis.BotApi.Application.Models;
using Aegis.BotApi.Application.UseCases;
using Aegis.BotApi.Contracts;
using Aegis.BotApi.Infrastructure.Auth;
using Aegis.BotApi.Mappers;
using Aegis.BotApi.Services;
using Aegis.Data.Entities;
using Aegis.Data.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Aegis.Tests;

public class BotApiMediaIntegrationTests
{
    [Fact]
    public async Task SendMedia_InvalidBase64_ShouldReturnValidationError()
    {
        var sut = CreateSut();
        var request = new SendMediaRequest(
            ChatId: "c:10",
            MediaBase64: "not-base64***",
            Caption: "bad media",
            MimeType: "image/jpeg");

        var command = sut.Mapper.Map(request);
        var response = await sut.UseCase.SendMessageAsync("token", command);

        Assert.False(response.Success);
        Assert.Equal(BotErrorCode.Validation, response.ErrorCode);
        Assert.Contains("base64", response.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        sut.MessageService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SendFile_InvalidBase64_ShouldReturnValidationError()
    {
        var sut = CreateSut();
        var request = new SendFileRequest(
            ChatId: "c:10",
            FileBase64: "bad-file-@@@",
            Caption: "invalid file",
            FileName: "broken.zip",
            MimeType: "application/zip");

        var command = sut.Mapper.Map(request);
        var response = await sut.UseCase.SendMessageAsync("token", command);

        Assert.False(response.Success);
        Assert.Equal(BotErrorCode.Validation, response.ErrorCode);
        Assert.Contains("base64", response.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        sut.MessageService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SendVoiceMessage_TooLargePayload_ShouldReturnValidationError()
    {
        var sut = CreateSut();
        var oversized = Convert.ToBase64String(new byte[(15 * 1024 * 1024) + 1]);
        var request = new SendVoiceMessageRequest(
            ChatId: "c:10",
            VoiceBase64: oversized,
            Caption: "too large",
            FileName: "voice.ogg",
            MimeType: "audio/ogg");

        var command = sut.Mapper.Map(request);
        var response = await sut.UseCase.SendMessageAsync("token", command);

        Assert.False(response.Success);
        Assert.Equal(BotErrorCode.Validation, response.ErrorCode);
        Assert.Contains("exceeds", response.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        sut.MessageService.VerifyNoOtherCalls();
    }

    private static TestHarness CreateSut()
    {
        var auth = new Mock<IBotAuthenticator>();
        auth.Setup(x => x.AuthenticateAsync(It.IsAny<string>()))
            .ReturnsAsync(new BotIdentity("test-bot", 777, true));

        var messageService = new Mock<IMessageService>(MockBehavior.Strict);
        var userSearch = new Mock<IUserSearchService>(MockBehavior.Strict);

        var mapper = new BotRequestMapper(new ChatIdResolver(), new RichContentFormatter());
        var useCase = new BotMessageUseCase(
            auth.Object,
            messageService.Object,
            userSearch.Object,
            NullLogger<BotMessageUseCase>.Instance);

        return new TestHarness(mapper, useCase, messageService);
    }

    private sealed record TestHarness(
        BotRequestMapper Mapper,
        BotMessageUseCase UseCase,
        Mock<IMessageService> MessageService);
}
