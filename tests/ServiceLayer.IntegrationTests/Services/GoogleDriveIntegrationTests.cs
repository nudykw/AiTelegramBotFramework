using Microsoft.Extensions.DependencyInjection;
using ServiceLayer.IntegrationTests.Fixtures;
using ServiceLayer.Services;
using ServiceLayer.Services.Mcp.GoogleDrive;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace ServiceLayer.IntegrationTests.Services;

[Trait("Service", "GoogleDriveService")]
public class GoogleDriveIntegrationTests : IClassFixture<TestAppFixture>
{
    private readonly IGoogleDriveService _driveService;
    private readonly AppSettings _appSettings;
    private const string TestUserId = "0";

    private string GetRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "GPTChatTelegramBot.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }

    private string ResolveSecretPath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return path;
        }
        return Path.GetFullPath(Path.Combine(GetRepoRoot(), path));
    }

    public GoogleDriveIntegrationTests(TestAppFixture fixture)
    {
        _appSettings = fixture.ServiceProvider.GetRequiredService<AppSettings>();

        var settings = _appSettings.GoogleDriveSettings;
        if (settings != null)
        {
            settings.ClientSecretsPath = ResolveSecretPath(settings.ClientSecretsPath);
            settings.ServiceAccountKeyPath = ResolveSecretPath(settings.ServiceAccountKeyPath);
            settings.TokenStorePath = ResolveSecretPath(settings.TokenStorePath);

            // Dynamically override AuthType to match the actual credential file present
            // on this machine. This prevents integration tests from failing due to the removal of
            // rotation, as only one credentials file might be present in a developer environment.
            bool hasOauth = File.Exists(settings.ClientSecretsPath);
            
            var serviceAccountPath = settings.ServiceAccountKeyPath;
            if (!File.Exists(serviceAccountPath))
            {
                var altPath = Path.Combine(Path.GetDirectoryName(serviceAccountPath) ?? "", "credentials.json");
                if (File.Exists(altPath))
                {
                    serviceAccountPath = altPath;
                }
            }
            bool hasServiceAccount = File.Exists(serviceAccountPath);

            if (hasOauth && !hasServiceAccount)
            {
                settings.AuthType = "OAuth";
            }
            else if (hasServiceAccount && !hasOauth)
            {
                settings.AuthType = "ServiceAccount";
            }
        }

        _driveService = fixture.ServiceProvider.GetRequiredService<IGoogleDriveService>();
    }

    [SkippableFact]
    public void CredentialsFolder_ShouldContainAtLeastOneKeyFile()
    {
        var settings = _appSettings.GoogleDriveSettings ?? new GoogleDriveSettings();
        var oauthPath = Path.GetFullPath(settings.ClientSecretsPath);
        var serviceAccountPath = Path.GetFullPath(settings.ServiceAccountKeyPath);

        if (!File.Exists(serviceAccountPath))
        {
            var altPath = Path.Combine(Path.GetDirectoryName(serviceAccountPath) ?? "", "credentials.json");
            if (File.Exists(altPath))
            {
                serviceAccountPath = altPath;
            }
        }

        bool hasOauth = File.Exists(oauthPath);
        bool hasServiceAccount = File.Exists(serviceAccountPath);

        if (!hasOauth && !hasServiceAccount)
        {
            throw new SkipException(
                "Skipped: Neither '.gdrive-server-credentials.json' (OAuth) nor '.credentials.json' (Service Account) " +
                "credentials files are found inside the '.secrets/' folder.");
        }

        Assert.True(hasOauth || hasServiceAccount);
    }

    private void SkipIfCredentialsNotConfigured()
    {
        var settings = _appSettings.GoogleDriveSettings ?? new GoogleDriveSettings();
        var oauthPath = Path.GetFullPath(settings.ClientSecretsPath);
        var serviceAccountPath = Path.GetFullPath(settings.ServiceAccountKeyPath);

        if (!File.Exists(serviceAccountPath))
        {
            var altPath = Path.Combine(Path.GetDirectoryName(serviceAccountPath) ?? "", "credentials.json");
            if (File.Exists(altPath))
            {
                serviceAccountPath = altPath;
            }
        }

        bool hasOauth = File.Exists(oauthPath);
        bool hasServiceAccount = File.Exists(serviceAccountPath);

        if (!hasOauth && !hasServiceAccount)
        {
            throw new SkipException(
                "Skipped: Neither '.gdrive-server-credentials.json' (OAuth) nor '.credentials.json' (Service Account) " +
                "credentials files are found inside the '.secrets/' folder.");
        }
    }

    [SkippableFact]
    public async Task EndToEndGoogleDriveOperations_Succeeds()
    {
        SkipIfCredentialsNotConfigured();

        // 1. List files (this will automatically verify/create the AppWorkspace_0 root folder)
        var initialList = await _driveService.ListAllAsync(TestUserId);
        Assert.NotNull(initialList);

        // 2. Create a new markdown file with mandatory description
        var fileName = $"integration_test_{Guid.NewGuid():N}.md";
        var fileDescription = "Temporary integration test file created by automated tests.";
        
        var createdItem = await _driveService.CreateItemAsync(
            userId: TestUserId,
            name: fileName,
            type: "file",
            description: fileDescription
        );

        Assert.NotNull(createdItem);
        Assert.NotNull(createdItem.Id);
        Assert.Equal(fileName, createdItem.Name);
        Assert.Equal("file", createdItem.Type);
        Assert.Equal(fileDescription, createdItem.Description);
        Assert.Equal($"/{fileName}", createdItem.VirtualPath);

        try
        {
            // 3. Verify it is now present in the sandbox directory list
            var currentList = await _driveService.ListAllAsync(TestUserId);
            var foundItem = currentList.FirstOrDefault(i => i.Id == createdItem.Id);
            Assert.NotNull(foundItem);
            Assert.Equal(fileName, foundItem.Name);

            // 4. Write new markdown content to the file
            var fileContent = "# E2E Integration Test\nThis is content written during integration testing.";
            var updatedItem = await _driveService.WriteFileContentAsync(
                userId: TestUserId,
                fileId: createdItem.Id,
                content: fileContent
            );

            Assert.NotNull(updatedItem);
            Assert.Equal(createdItem.Id, updatedItem.Id);

            // 5. Read back and assert the content matches
            var (readContent, readItem) = await _driveService.ReadFileContentAsync(TestUserId, createdItem.Id);
            Assert.Equal(fileContent, readContent);
            Assert.NotNull(readItem);
            Assert.Equal(createdItem.Id, readItem.Id);

            // 6. Update the file description
            var newDescription = "Updated integration test description.";
            var descriptionUpdatedItem = await _driveService.UpdateDescriptionAsync(
                userId: TestUserId,
                id: createdItem.Id,
                newDescription: newDescription
            );

            Assert.NotNull(descriptionUpdatedItem);
            Assert.Equal(newDescription, descriptionUpdatedItem.Description);
        }
        finally
        {
            // 7. Clean up: trash the created file
            var isDeleted = await _driveService.DeleteItemAsync(TestUserId, createdItem.Id);
            Assert.True(isDeleted);

            // 8. Confirm it is no longer returned in the sandbox list
            var finalList = await _driveService.ListAllAsync(TestUserId);
            var stillExists = finalList.Any(i => i.Id == createdItem.Id);
            Assert.False(stillExists);
        }
    }
}
