using Aegis.Common;
using Aegis.Data.Entities;
using Aegis.Data.Repositories;
using Aegis.Data.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Aegis.Tests;

public class AppCredentialServiceTests
{
    [Fact]
    public async Task ValidateCredentialsAsync_ShouldAcceptBuiltInOfficialCredentials()
    {
        var repository = new Mock<IAppCredentialRepository>(MockBehavior.Strict);
        var service = new AppCredentialService(repository.Object, NullLogger<AppCredentialService>.Instance);

        var credential = await service.ValidateCredentialsAsync(
            OfficialClientCredentials.AppId,
            OfficialClientCredentials.AppHash);

        Assert.NotNull(credential);
        Assert.Equal(OfficialClientCredentials.AppId, credential.AppId);
        Assert.Equal(OfficialClientCredentials.ShortName, credential.ShortName);
    }

    [Fact]
    public async Task ValidateCredentialsAsync_ShouldFallbackToRepositoryForNonOfficialCredentials()
    {
        var expected = new AppCredential
        {
            AppId = 42,
            AppHash = "custom-app-hash",
            ShortName = "custom_app",
            AppTitle = "Custom App",
            Platform = "desktop",
            OwnerId = 10,
            IsActive = true
        };

        var repository = new Mock<IAppCredentialRepository>();
        repository
            .Setup(r => r.ValidateAsync(42, "custom-app-hash"))
            .ReturnsAsync(expected);

        var service = new AppCredentialService(repository.Object, NullLogger<AppCredentialService>.Instance);

        var credential = await service.ValidateCredentialsAsync(42, "custom-app-hash");

        Assert.Same(expected, credential);
        repository.Verify(r => r.ValidateAsync(42, "custom-app-hash"), Times.Once);
    }
}