using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Http;
using Google.Apis.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceLayer.Services;
using ServiceLayer.Services.Mcp.GoogleDrive;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace ServiceLayer.UnitTests.Services.Mcp;

public class GoogleDriveAuthServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _serviceAccountPath;
    private readonly string _clientSecretsPath;

    public GoogleDriveAuthServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _serviceAccountPath = Path.Combine(_tempDir, ".credentials.json");
        _clientSecretsPath = Path.Combine(_tempDir, ".gdrive-server-credentials.json");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }

    private class TestableGoogleDriveAuthService : GoogleDriveAuthService
    {
        public bool OAuthLoaded { get; private set; }
        public bool ServiceAccountLoaded { get; private set; }
        public bool DriveServiceCreated { get; private set; }

        public TestableGoogleDriveAuthService(AppSettings settings)
            : base(settings, NullLogger<GoogleDriveAuthService>.Instance)
        {
        }

        protected override Task<IConfigurableHttpClientInitializer> LoadOAuthCredentialAsync(GoogleDriveSettings config, string secretsPath)
        {
            OAuthLoaded = true;
            return Task.FromResult(new Mock<IConfigurableHttpClientInitializer>().Object);
        }

        protected override Task<IConfigurableHttpClientInitializer> LoadServiceAccountCredentialAsync(GoogleDriveSettings config, string keyPath)
        {
            ServiceAccountLoaded = true;
            return Task.FromResult(new Mock<IConfigurableHttpClientInitializer>().Object);
        }

        protected override DriveService CreateDriveService(IConfigurableHttpClientInitializer credential)
        {
            DriveServiceCreated = true;
            return new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "GptChatTelegramBotGoogleDriveMCP"
            });
        }

        protected override Task VerifyServiceAsync(DriveService service)
        {
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task GetDriveServiceAsync_ShouldThrowFileNotFound_WhenServiceAccountFileMissing()
    {
        // Arrange
        var settings = new AppSettings
        {
            GoogleDriveSettings = new GoogleDriveSettings
            {
                AuthType = "ServiceAccount",
                ServiceAccountKeyPath = _serviceAccountPath
            }
        };

        var service = new TestableGoogleDriveAuthService(settings);

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() => service.GetDriveServiceAsync());
    }

    [Fact]
    public async Task GetDriveServiceAsync_ShouldThrowFileNotFound_WhenOAuthFileMissing()
    {
        // Arrange
        var settings = new AppSettings
        {
            GoogleDriveSettings = new GoogleDriveSettings
            {
                AuthType = "OAuth",
                ClientSecretsPath = _clientSecretsPath
            }
        };

        var service = new TestableGoogleDriveAuthService(settings);

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() => service.GetDriveServiceAsync());
    }

    [Fact]
    public async Task GetDriveServiceAsync_ShouldThrowInvalidOperation_WhenInvalidAuthType()
    {
        // Arrange
        var settings = new AppSettings
        {
            GoogleDriveSettings = new GoogleDriveSettings
            {
                AuthType = "InvalidType"
            }
        };

        var service = new TestableGoogleDriveAuthService(settings);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetDriveServiceAsync());
    }

    [Fact]
    public async Task GetDriveServiceAsync_ShouldInitializeServiceAccount_WhenValidJson()
    {
        // Arrange
        await File.WriteAllTextAsync(_serviceAccountPath, "{}");

        var settings = new AppSettings
        {
            GoogleDriveSettings = new GoogleDriveSettings
            {
                AuthType = "ServiceAccount",
                ServiceAccountKeyPath = _serviceAccountPath
            }
        };

        var service = new TestableGoogleDriveAuthService(settings);

        // Act
        var driveService = await service.GetDriveServiceAsync();

        // Assert
        Assert.NotNull(driveService);
        Assert.True(service.ServiceAccountLoaded);
        Assert.False(service.OAuthLoaded); // No rotation/fallback to OAuth
        Assert.True(service.DriveServiceCreated);

        // Verify caching works (second call returns exact same instance)
        var secondDriveService = await service.GetDriveServiceAsync();
        Assert.Same(driveService, secondDriveService);
    }

    [Fact]
    public async Task GetDriveServiceAsync_ShouldInitializeOAuth_WhenValidSecrets()
    {
        // Arrange
        await File.WriteAllTextAsync(_clientSecretsPath, "{}");

        var settings = new AppSettings
        {
            GoogleDriveSettings = new GoogleDriveSettings
            {
                AuthType = "OAuth",
                ClientSecretsPath = _clientSecretsPath
            }
        };

        var service = new TestableGoogleDriveAuthService(settings);

        // Act
        var driveService = await service.GetDriveServiceAsync();

        // Assert
        Assert.NotNull(driveService);
        Assert.True(service.OAuthLoaded);
        Assert.False(service.ServiceAccountLoaded); // No rotation/fallback to ServiceAccount
        Assert.True(service.DriveServiceCreated);
    }

    [Fact]
    public async Task GetDriveServiceAsync_ShouldDefaultToOAuth_WhenAuthTypeIsEmptyOrNull()
    {
        // Arrange
        await File.WriteAllTextAsync(_clientSecretsPath, "{}");

        var settings = new AppSettings
        {
            GoogleDriveSettings = new GoogleDriveSettings
            {
                AuthType = null, // Null should default to OAuth
                ClientSecretsPath = _clientSecretsPath
            }
        };

        var service = new TestableGoogleDriveAuthService(settings);

        // Act
        var driveService = await service.GetDriveServiceAsync();

        // Assert
        Assert.NotNull(driveService);
        Assert.True(service.OAuthLoaded);
        Assert.False(service.ServiceAccountLoaded); // Must not attempt ServiceAccount
        Assert.True(service.DriveServiceCreated);
    }

    [Fact]
    public async Task GetDriveServiceAsync_ShouldThrowFileNotFound_WhenDefaultingToOAuthAndOAuthFileMissing()
    {
        // Arrange
        var settings = new AppSettings
        {
            GoogleDriveSettings = new GoogleDriveSettings
            {
                AuthType = "", // Empty should default to OAuth
                ClientSecretsPath = _clientSecretsPath
            }
        };

        var service = new TestableGoogleDriveAuthService(settings);

        // Act & Assert
        // Should throw FileNotFoundException for OAuth file directly, without attempting ServiceAccount
        await Assert.ThrowsAsync<FileNotFoundException>(() => service.GetDriveServiceAsync());
        Assert.False(service.ServiceAccountLoaded);
    }
}
