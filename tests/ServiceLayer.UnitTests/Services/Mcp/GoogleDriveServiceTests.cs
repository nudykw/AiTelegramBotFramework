using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using DriveFile = Google.Apis.Drive.v3.Data.File;
using Google.Apis.Http;
using Google.Apis.Services;
using Google.Apis.Upload;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceLayer.Services;
using ServiceLayer.Services.Mcp;
using ServiceLayer.Services.Mcp.GoogleDrive;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ServiceLayer.UnitTests.Services.Mcp;

public class GoogleDriveServiceTests
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Handler { get; set; } = req => 
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(Handler(request));
        }
    }

    private class FakeHttpClientFactory : Google.Apis.Http.IHttpClientFactory
    {
        private readonly MockHttpMessageHandler _handler;

        public FakeHttpClientFactory(MockHttpMessageHandler handler)
        {
            _handler = handler;
        }

        public ConfigurableHttpClient CreateHttpClient(CreateHttpClientArgs args)
        {
            var client = new ConfigurableHttpClient(new ConfigurableMessageHandler(_handler));
            client.BaseAddress = new Uri("https://www.googleapis.com/");
            return client;
        }
    }

    private class TestableGoogleDriveService : GoogleDriveService
    {
        public DriveFile? MockResponseBody { get; set; }
        public Action<ResumableUpload>? OnUpload { get; set; }
        public bool CallBaseUpload { get; set; }

        public TestableGoogleDriveService(
            IGoogleDriveAuthService authService,
            AppSettings settings)
            : base(authService, settings, NullLogger<GoogleDriveService>.Instance)
        {
        }

        protected override async Task UploadFileMediaAsync(ResumableUpload upload)
        {
            OnUpload?.Invoke(upload);

            if (CallBaseUpload)
            {
                await base.UploadFileMediaAsync(upload);
                return;
            }

            if (MockResponseBody != null)
            {
                var t = upload.GetType();
                bool setSuccessful = false;
                while (t != null)
                {
                    var field = t.GetField("<ResponseBody>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (field != null)
                    {
                        field.SetValue(upload, MockResponseBody);
                        setSuccessful = true;
                        break;
                    }
                    t = t.BaseType;
                }
                if (!setSuccessful)
                {
                    throw new InvalidOperationException("Could not find backing field <ResponseBody>k__BackingField in upload class hierarchy.");
                }
            }
        }
    }

    private (TestableGoogleDriveService Service, MockHttpMessageHandler HttpHandler) CreateServiceWithMockHttp(AppSettings settings)
    {
        var httpHandler = new MockHttpMessageHandler();
        var clientFactory = new FakeHttpClientFactory(httpHandler);

        var driveService = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = new Mock<IConfigurableHttpClientInitializer>().Object,
            ApplicationName = "Test",
            HttpClientFactory = clientFactory
        });

        var mockAuthService = new Mock<IGoogleDriveAuthService>();
        mockAuthService.Setup(a => a.GetDriveServiceAsync()).ReturnsAsync(driveService);

        var service = new TestableGoogleDriveService(
            mockAuthService.Object,
            settings
        );

        return (service, httpHandler);
    }

    [Fact]
    public async Task ListAllAsync_ShouldCreateRootFolder_WhenItDoesNotExist()
    {
        // Arrange
        var settings = new AppSettings();
        var (service, httpHandler) = CreateServiceWithMockHttp(settings);
        using var context = McpContext.Symbolize(12345, 12345);

        var calls = new List<string>();

        httpHandler.Handler = req =>
        {
            var url = req.RequestUri?.ToString() ?? "";
            calls.Add($"{req.Method} {url}");

            if (req.Method == HttpMethod.Get && url.Contains("q=mimeType"))
            {
                // Root folder search: return empty files list (does not exist)
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(@"{ ""files"": [] }")
                };
            }
            if (req.Method == HttpMethod.Post && url.Contains("/files"))
            {
                // Root folder creation: return created folder ID
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(@"{ ""id"": ""new_root_folder_id"", ""name"": ""AppWorkspace_12345"" }")
                };
            }
            if (req.Method == HttpMethod.Get && url.Contains("parents"))
            {
                if (url.Contains("new_root_folder_id"))
                {
                    // Children query of new_root_folder_id: return subfolders & files
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(@"{ ""files"": [
                            { ""id"": ""subfolder_id"", ""name"": ""docs"", ""mimeType"": ""application/vnd.google-apps.folder"", ""description"": ""Folder description"" },
                            { ""id"": ""markdown_id"", ""name"": ""readme.md"", ""mimeType"": ""text/markdown"", ""description"": ""File description"" },
                            { ""id"": ""ignored_id"", ""name"": ""image.png"", ""mimeType"": ""image/png"" }
                        ] }")
                    };
                }
                else
                {
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(@"{ ""files"": [] }") };
                }
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(@"{ ""files"": [] }") };
        };

        // Act
        var items = await service.ListAllAsync(null);

        // Assert
        Assert.NotNull(items);
        Assert.Equal(2, items.Count); // folder & md file, ignored png file

        var folder = items.Find(i => i.Id == "subfolder_id");
        Assert.NotNull(folder);
        Assert.Equal("docs", folder.Name);
        Assert.Equal("folder", folder.Type);
        Assert.Equal("/docs", folder.VirtualPath);
        Assert.Equal("Folder description", folder.Description);

        var file = items.Find(i => i.Id == "markdown_id");
        Assert.NotNull(file);
        Assert.Equal("readme.md", file.Name);
        Assert.Equal("file", file.Type);
        Assert.Equal("/readme.md", file.VirtualPath);
        Assert.Equal("File description", file.Description);

        Assert.Contains(calls, c => c.StartsWith("POST https://www.googleapis.com/drive/v3/files"));
    }

    [Fact]
    public async Task ListAllAsync_ShouldFilterNonRecursiveDirectChildren_WhenIsRecursiveFalse()
    {
        // Arrange
        var settings = new AppSettings();
        var (service, httpHandler) = CreateServiceWithMockHttp(settings);
        using var context = McpContext.Symbolize(12345, 12345);

        httpHandler.Handler = req =>
        {
            var url = req.RequestUri?.ToString() ?? "";
            if (url.Contains("q=mimeType"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(@"{ ""files"": [ { ""id"": ""root_id"" } ] }") };
            }
            if (url.Contains("parents"))
            {
                if (url.Contains("root_id"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(@"{ ""files"": [
                            { ""id"": ""folder_docs_id"", ""name"": ""docs"", ""mimeType"": ""application/vnd.google-apps.folder"", ""description"": ""Docs folder"" },
                            { ""id"": ""file_readme_id"", ""name"": ""readme.md"", ""mimeType"": ""text/markdown"", ""description"": ""Readme"" }
                        ] }")
                    };
                }
                else if (url.Contains("folder_docs_id"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(@"{ ""files"": [
                            { ""id"": ""file_subnote_id"", ""name"": ""subnote.md"", ""mimeType"": ""text/markdown"", ""description"": ""Subnote"" }
                        ] }")
                    };
                }
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(@"{ ""files"": [] }") };
        };

        // Act - List root recursively = false
        var rootDirectItems = await service.ListAllAsync(null, folderId: null, isRecursive: false);

        // Assert
        Assert.Equal(2, rootDirectItems.Count);
        Assert.Contains(rootDirectItems, i => i.Id == "folder_docs_id");
        Assert.Contains(rootDirectItems, i => i.Id == "file_readme_id");
        Assert.DoesNotContain(rootDirectItems, i => i.Id == "file_subnote_id");

        // Act - List folder_docs_id recursively = false
        var docsDirectItems = await service.ListAllAsync(null, folderId: "folder_docs_id", isRecursive: false);

        // Assert
        Assert.Single(docsDirectItems);
        Assert.Equal("file_subnote_id", docsDirectItems[0].Id);
    }

    [Fact]
    public async Task ListAllAsync_ShouldHideDescriptions_WhenIncludeDescriptionFalse()
    {
        // Arrange
        var settings = new AppSettings();
        var (service, httpHandler) = CreateServiceWithMockHttp(settings);
        using var context = McpContext.Symbolize(12345, 12345);

        httpHandler.Handler = req =>
        {
            var url = req.RequestUri?.ToString() ?? "";
            if (url.Contains("q=mimeType"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(@"{ ""files"": [ { ""id"": ""root_id"" } ] }") };
            }
            if (url.Contains("parents"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(@"{ ""files"": [
                        { ""id"": ""file_readme_id"", ""name"": ""readme.md"", ""mimeType"": ""text/markdown"", ""description"": ""Readme description"" }
                    ] }")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(@"{ ""files"": [] }") };
        };

        // Act - list with includeDescription = false
        var items = await service.ListAllAsync(null, folderId: null, isRecursive: true, includeDescription: false);

        // Assert
        Assert.Single(items);
        Assert.Null(items[0].Description);
    }

    [Fact]
    public async Task ListAllAsync_ShouldThrowUnauthorized_WhenFolderIdOutsideSandbox()
    {
        // Arrange
        var settings = new AppSettings();
        var (service, httpHandler) = CreateServiceWithMockHttp(settings);
        using var context = McpContext.Symbolize(12345, 12345);

        httpHandler.Handler = req =>
        {
            var url = req.RequestUri?.ToString() ?? "";
            if (url.Contains("q=mimeType"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(@"{ ""files"": [ { ""id"": ""root_id"" } ] }") };
            }
            if (url.Contains("parents"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(@"{ ""files"": [
                        { ""id"": ""file_readme_id"", ""name"": ""readme.md"", ""mimeType"": ""text/markdown"", ""description"": ""Readme"" }
                    ] }")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(@"{ ""files"": [] }") };
        };

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ListAllAsync(null, folderId: "invalid_unauthorized_folder_id"));
    }

    [Fact]
    public async Task ReadFileContentAsync_ShouldThrowUnauthorized_WhenFileNotInSandbox()
    {
        // Arrange
        var settings = new AppSettings();
        var (service, httpHandler) = CreateServiceWithMockHttp(settings);
        using var context = McpContext.Symbolize(12345, 12345);

        httpHandler.Handler = req =>
        {
            // Empty list
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(@"{ ""files"": [] }")
            };
        };

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ReadFileContentAsync(null, "invalid_file_id"));
    }

    [Fact]
    public async Task ReadFileContentAsync_ShouldDownloadContent_WhenFileExistsInSandbox()
    {
        // Arrange
        var settings = new AppSettings();
        var (service, httpHandler) = CreateServiceWithMockHttp(settings);
        using var context = McpContext.Symbolize(12345, 12345);

        httpHandler.Handler = req =>
        {
            var url = req.RequestUri?.ToString() ?? "";
            if (url.Contains("q=mimeType"))
            {
                // root folder found
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(@"{ ""files"": [ { ""id"": ""root_id"" } ] }")
                };
            }
            if (url.Contains("parents"))
            {
                // Children: contains our file
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(@"{ ""files"": [ { ""id"": ""file_id"", ""name"": ""note.md"", ""mimeType"": ""text/markdown"" } ] }")
                };
            }
            if (url.Contains("/files/file_id") && url.Contains("alt=media"))
            {
                // Content download
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("Markdown Content")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        // Act
        var (content, item) = await service.ReadFileContentAsync(null, "file_id");

        // Assert
        Assert.Equal("Markdown Content", content);
        Assert.Equal("file_id", item.Id);
    }

    [Fact]
    public async Task WriteFileContentAsync_ShouldUploadContent_WhenFileExistsInSandbox()
    {
        // Arrange
        var settings = new AppSettings();
        var (service, httpHandler) = CreateServiceWithMockHttp(settings);
        using var context = McpContext.Symbolize(12345, 12345);

        var uploadCalled = false;
        service.OnUpload = upload => { uploadCalled = true; };

        httpHandler.Handler = req =>
        {
            var url = req.RequestUri?.ToString() ?? "";
            if (url.Contains("q=mimeType"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(@"{ ""files"": [ { ""id"": ""root_id"" } ] }")
                };
            }
            if (url.Contains("parents"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(@"{ ""files"": [ { ""id"": ""file_id"", ""name"": ""note.md"", ""mimeType"": ""text/markdown"" } ] }")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        // Act
        var item = await service.WriteFileContentAsync(null, "file_id", "# New Content");

        // Assert
        Assert.NotNull(item);
        Assert.Equal("file_id", item.Id);
        Assert.True(uploadCalled);
    }

    [Fact]
    public async Task CreateItemAsync_ShouldThrowArgument_WhenInvalidArgs()
    {
        // Arrange
        var settings = new AppSettings();
        var (service, _) = CreateServiceWithMockHttp(settings);

        // Name cannot be empty
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateItemAsync(null, "", "file", "desc"));
        // Description cannot be empty
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateItemAsync(null, "a.md", "file", ""));
        // Type must be file/folder
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateItemAsync(null, "a.md", "invalid", "desc"));
        // Traversal characters in name
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateItemAsync(null, "../a.md", "file", "desc"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateItemAsync(null, "docs/a.md", "file", "desc"));
    }

    [Fact]
    public async Task CreateItemAsync_ShouldCreateFileInRoot_WhenParentIdNull()
    {
        // Arrange
        var settings = new AppSettings();
        var (service, httpHandler) = CreateServiceWithMockHttp(settings);
        using var context = McpContext.Symbolize(12345, 12345);

        service.MockResponseBody = new DriveFile
        {
            Id = "new_file_id",
            Name = "note.md",
            MimeType = "text/markdown",
            Description = "My note"
        };

        httpHandler.Handler = req =>
        {
            var url = req.RequestUri?.ToString() ?? "";
            if (url.Contains("q=mimeType"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(@"{ ""files"": [ { ""id"": ""root_id"" } ] }")
                };
            }
            if (url.Contains("parents"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(@"{ ""files"": [] }")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        // Act
        var item = await service.CreateItemAsync(null, "note.md", "file", "My note");

        // Assert
        Assert.NotNull(item);
        Assert.Equal("new_file_id", item.Id);
        Assert.Equal("/note.md", item.VirtualPath);
        Assert.Equal("My note", item.Description);
    }

    [Fact]
    public async Task CreateItemAsync_ShouldCreateFolderInParent_WhenParentIdProvided()
    {
        // Arrange
        var settings = new AppSettings();
        var (service, httpHandler) = CreateServiceWithMockHttp(settings);
        using var context = McpContext.Symbolize(12345, 12345);

        httpHandler.Handler = req =>
        {
            var url = req.RequestUri?.ToString() ?? "";
            if (url.Contains("q=mimeType"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(@"{ ""files"": [ { ""id"": ""root_id"" } ] }")
                };
            }
            if (url.Contains("parents"))
            {
                if (url.Contains("root_id"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(@"{ ""files"": [ { ""id"": ""parent_folder_id"", ""name"": ""docs"", ""mimeType"": ""application/vnd.google-apps.folder"" } ] }")
                    };
                }
                else
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(@"{ ""files"": [] }")
                    };
                }
            }
            if (req.Method == HttpMethod.Post && url.Contains("/files"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(@"{ ""id"": ""new_folder_id"", ""name"": ""subdocs"", ""mimeType"": ""application/vnd.google-apps.folder"", ""description"": ""Sub folder"" }")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        // Act
        var item = await service.CreateItemAsync(null, "subdocs", "folder", "Sub folder", "parent_folder_id");

        // Assert
        Assert.NotNull(item);
        Assert.Equal("new_folder_id", item.Id);
        Assert.Equal("/docs/subdocs", item.VirtualPath);
        Assert.Equal("Sub folder", item.Description);
    }

    [Fact]
    public async Task CreateItemAsync_ShouldThrowInvalidOperation_WhenFileAlreadyExists()
    {
        // Arrange
        var settings = new AppSettings();
        var (service, httpHandler) = CreateServiceWithMockHttp(settings);
        using var context = McpContext.Symbolize(12345, 12345);

        httpHandler.Handler = req =>
        {
            var url = req.RequestUri?.ToString() ?? "";
            if (url.Contains("q=mimeType"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(@"{ ""files"": [ { ""id"": ""root_id"" } ] }")
                };
            }
            if (url.Contains("parents"))
            {
                if (url.Contains("note.md"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(@"{ ""files"": [ { ""id"": ""existing_file_id"", ""name"": ""note.md"", ""mimeType"": ""text/markdown"" } ] }")
                    };
                }
                
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(@"{ ""files"": [] }")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateItemAsync(null, "note.md", "file", "My note"));
        Assert.Equal("Error: A file named 'note.md' already exists in this directory. Use the 'gdrive_write' tool to update its content instead of creating a new one.", ex.Message);
    }

    [Fact]
    public async Task CreateItemAsync_ShouldThrowInvalidOperation_WhenFolderAlreadyExists()
    {
        // Arrange
        var settings = new AppSettings();
        var (service, httpHandler) = CreateServiceWithMockHttp(settings);
        using var context = McpContext.Symbolize(12345, 12345);

        httpHandler.Handler = req =>
        {
            var url = req.RequestUri?.ToString() ?? "";
            if (url.Contains("q=mimeType"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(@"{ ""files"": [ { ""id"": ""root_id"" } ] }")
                };
            }
            if (url.Contains("parents"))
            {
                if (url.Contains("subdocs"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(@"{ ""files"": [ { ""id"": ""existing_folder_id"", ""name"": ""subdocs"", ""mimeType"": ""application/vnd.google-apps.folder"" } ] }")
                    };
                }
                
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(@"{ ""files"": [] }")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateItemAsync(null, "subdocs", "folder", "Sub folder"));
        Assert.Equal("Error: A folder named 'subdocs' already exists in this directory.", ex.Message);
    }

    [Fact]
    public async Task UpdateDescriptionAsync_ShouldUpdateMetadataDescription_WhenItemInSandbox()
    {
        // Arrange
        var settings = new AppSettings();
        var (service, httpHandler) = CreateServiceWithMockHttp(settings);
        using var context = McpContext.Symbolize(12345, 12345);

        httpHandler.Handler = req =>
        {
            var url = req.RequestUri?.ToString() ?? "";
            if (url.Contains("q=mimeType"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(@"{ ""files"": [ { ""id"": ""root_id"" } ] }")
                };
            }
            if (url.Contains("parents"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(@"{ ""files"": [ { ""id"": ""file_id"", ""name"": ""note.md"", ""mimeType"": ""text/markdown"" } ] }")
                };
            }
            if (req.Method == HttpMethod.Patch && url.Contains("/files/file_id"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(@"{ ""id"": ""file_id"", ""name"": ""note.md"", ""description"": ""New description"" }")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        // Act
        var item = await service.UpdateDescriptionAsync(null, "file_id", "New description");

        // Assert
        Assert.NotNull(item);
        Assert.Equal("file_id", item.Id);
        Assert.Equal("New description", item.Description);
    }

    [Fact]
    public async Task DeleteItemAsync_ShouldThrowInvalidOperation_WhenDeletingRootFolder()
    {
        // Arrange
        var settings = new AppSettings();
        var (service, httpHandler) = CreateServiceWithMockHttp(settings);
        using var context = McpContext.Symbolize(12345, 12345);

        httpHandler.Handler = req =>
        {
            if (req.RequestUri?.ToString().Contains("q=mimeType") == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(@"{ ""files"": [ { ""id"": ""root_id"" } ] }")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteItemAsync(null, "root_id"));
    }

    [Fact]
    public async Task DeleteItemAsync_ShouldSetTrashedTrue_WhenItemInSandbox()
    {
        // Arrange
        var settings = new AppSettings();
        var (service, httpHandler) = CreateServiceWithMockHttp(settings);
        using var context = McpContext.Symbolize(12345, 12345);

        httpHandler.Handler = req =>
        {
            var url = req.RequestUri?.ToString() ?? "";
            if (url.Contains("q=mimeType"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(@"{ ""files"": [ { ""id"": ""root_id"" } ] }")
                };
            }
            if (url.Contains("parents"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(@"{ ""files"": [ { ""id"": ""file_id"", ""name"": ""note.md"", ""mimeType"": ""text/markdown"" } ] }")
                };
            }
            if (req.Method == HttpMethod.Patch && url.Contains("/files/file_id"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(@"{ ""id"": ""file_id"", ""name"": ""note.md"", ""trashed"": true }")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        // Act
        var result = await service.DeleteItemAsync(null, "file_id");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task WriteFileContentAsync_ShouldThrowInvalidOperation_WhenUploadFails()
    {
        // Arrange
        var settings = new AppSettings();
        var (service, httpHandler) = CreateServiceWithMockHttp(settings);
        using var context = McpContext.Symbolize(12345, 12345);

        service.CallBaseUpload = true;

        httpHandler.Handler = req =>
        {
            var url = req.RequestUri?.ToString() ?? "";
            if (url.Contains("q=mimeType"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(@"{ ""files"": [ { ""id"": ""root_id"" } ] }")
                };
            }
            if (url.Contains("parents"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(@"{ ""files"": [ { ""id"": ""file_id"", ""name"": ""note.md"", ""mimeType"": ""text/markdown"" } ] }")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.BadRequest);
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.WriteFileContentAsync(null, "file_id", "# New Content"));
        Assert.Contains("Failed to upload Google Drive file", ex.Message);
    }

    [Fact]
    public async Task ListAllAsync_ShouldReturnTrashedItems_OnlyWhenIncludeTrashedTrue()
    {
        // Arrange
        var settings = new AppSettings();
        var (service, httpHandler) = CreateServiceWithMockHttp(settings);
        using var context = McpContext.Symbolize(12345, 12345);

        httpHandler.Handler = req =>
        {
            var url = req.RequestUri?.ToString() ?? "";
            if (url.Contains("q=mimeType"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(@"{ ""files"": [ { ""id"": ""root_id"" } ] }") };
            }
            if (url.Contains("parents"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(@"{ ""files"": [
                        { ""id"": ""file_active_id"", ""name"": ""active.md"", ""mimeType"": ""text/markdown"", ""description"": ""Active file"" },
                        { ""id"": ""file_trashed_id"", ""name"": ""trashed.md"", ""mimeType"": ""text/markdown"", ""description"": ""Trashed file"", ""trashed"": true }
                    ] }")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(@"{ ""files"": [] }") };
        };

        // Act - includeTrashed = false
        var activeItems = await service.ListAllAsync(null, folderId: null, isRecursive: true, includeDescription: true, includeTrashed: false);

        // Assert
        Assert.Single(activeItems);
        Assert.Equal("file_active_id", activeItems[0].Id);
        Assert.False(activeItems[0].IsTrashed);

        // Act - includeTrashed = true
        var allItems = await service.ListAllAsync(null, folderId: null, isRecursive: true, includeDescription: true, includeTrashed: true);

        // Assert
        Assert.Equal(2, allItems.Count);
        var active = allItems.Find(i => i.Id == "file_active_id");
        var trashed = allItems.Find(i => i.Id == "file_trashed_id");
        Assert.NotNull(active);
        Assert.False(active.IsTrashed);
        Assert.NotNull(trashed);
        Assert.True(trashed.IsTrashed);
    }

    [Fact]
    public async Task RestoreItemAsync_ShouldSetTrashedFalse_WhenItemInSandbox()
    {
        // Arrange
        var settings = new AppSettings();
        var (service, httpHandler) = CreateServiceWithMockHttp(settings);
        using var context = McpContext.Symbolize(12345, 12345);

        httpHandler.Handler = req =>
        {
            var url = req.RequestUri?.ToString() ?? "";
            if (url.Contains("q=mimeType"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(@"{ ""files"": [ { ""id"": ""root_id"" } ] }")
                };
            }
            if (url.Contains("parents"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(@"{ ""files"": [ { ""id"": ""file_id"", ""name"": ""note.md"", ""mimeType"": ""text/markdown"", ""trashed"": true } ] }")
                };
            }
            if (req.Method == HttpMethod.Patch && url.Contains("/files/file_id"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(@"{ ""id"": ""file_id"", ""name"": ""note.md"", ""trashed"": false, ""size"": 42, ""md5Checksum"": ""hash123"" }")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        // Act
        var result = await service.RestoreItemAsync(null, "file_id");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("file_id", result.Id);
        Assert.False(result.IsTrashed);
        Assert.Equal(42, result.Size);
        Assert.Equal("hash123", result.Md5Checksum);
    }

    // ── Version control tests ────────────────────────────────────────────────

    [Fact]
    public async Task ListRevisionsAsync_ShouldThrowUnauthorized_WhenFileNotInSandbox()
    {
        // Arrange
        var settings = new AppSettings();
        var (service, httpHandler) = CreateServiceWithMockHttp(settings);
        using var context = McpContext.Symbolize(12345, 12345);

        httpHandler.Handler = req =>
        {
            var url = req.RequestUri?.ToString() ?? "";
            if (url.Contains("q=mimeType"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(@"{ ""files"": [ { ""id"": ""root_id"" } ] }") };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(@"{ ""files"": [] }") };
        };

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ListRevisionsAsync(null, "unknown_file_id"));
    }

    [Fact]
    public async Task ListRevisionsAsync_ShouldReturnRevisionList_WhenFileInSandbox()
    {
        // Arrange
        var settings = new AppSettings();
        var (service, httpHandler) = CreateServiceWithMockHttp(settings);
        using var context = McpContext.Symbolize(12345, 12345);

        httpHandler.Handler = req =>
        {
            var url = req.RequestUri?.ToString() ?? "";
            if (url.Contains("q=mimeType"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(@"{ ""files"": [ { ""id"": ""root_id"" } ] }") };
            if (url.Contains("parents"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(@"{ ""files"": [ { ""id"": ""file_id"", ""name"": ""note.md"", ""mimeType"": ""text/markdown"" } ] }") };
            if (url.Contains("/files/file_id/revisions") && !url.Contains("alt=media"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(@"{ ""revisions"": [
                        { ""id"": ""rev_1"", ""modifiedTime"": ""2025-01-01T00:00:00Z"", ""size"": 100, ""keepForever"": false },
                        { ""id"": ""rev_2"", ""modifiedTime"": ""2025-06-01T00:00:00Z"", ""size"": 200, ""keepForever"": true }
                    ] }")
                };
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        // Act
        var revisions = await service.ListRevisionsAsync(null, "file_id");

        // Assert
        Assert.Equal(2, revisions.Count);
        Assert.Equal("rev_1", revisions[0].Id);
        Assert.Equal("rev_2", revisions[1].Id);
        Assert.True(revisions[1].KeepForever);
    }

    [Fact]
    public async Task ReadRevisionContentAsync_ShouldThrowUnauthorized_WhenFileNotInSandbox()
    {
        // Arrange
        var settings = new AppSettings();
        var (service, httpHandler) = CreateServiceWithMockHttp(settings);
        using var context = McpContext.Symbolize(12345, 12345);

        httpHandler.Handler = req =>
        {
            var url = req.RequestUri?.ToString() ?? "";
            if (url.Contains("q=mimeType"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(@"{ ""files"": [ { ""id"": ""root_id"" } ] }") };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(@"{ ""files"": [] }") };
        };

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ReadRevisionContentAsync(null, "unknown_file_id", "rev_1"));
    }

    [Fact]
    public async Task ReadRevisionContentAsync_ShouldReturnContent_WhenRevisionExists()
    {
        // Arrange
        var settings = new AppSettings();
        var (service, httpHandler) = CreateServiceWithMockHttp(settings);
        using var context = McpContext.Symbolize(12345, 12345);

        httpHandler.Handler = req =>
        {
            var url = req.RequestUri?.ToString() ?? "";
            if (url.Contains("q=mimeType"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(@"{ ""files"": [ { ""id"": ""root_id"" } ] }") };
            if (url.Contains("parents"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(@"{ ""files"": [ { ""id"": ""file_id"", ""name"": ""note.md"", ""mimeType"": ""text/markdown"" } ] }") };
            // Revision metadata (GET without alt=media)
            if (url.Contains("/files/file_id/revisions/rev_1") && !url.Contains("alt=media"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(@"{ ""id"": ""rev_1"", ""modifiedTime"": ""2025-01-01T00:00:00Z"", ""size"": 42, ""keepForever"": false }")
                };
            // Revision content download (GET with alt=media)
            if (url.Contains("/files/file_id/revisions/rev_1") && url.Contains("alt=media"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("# Historical content") };
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        // Act
        var (content, revision) = await service.ReadRevisionContentAsync(null, "file_id", "rev_1");

        // Assert
        Assert.Equal("# Historical content", content);
        Assert.Equal("rev_1", revision.Id);
        Assert.Equal(42, revision.Size);
    }

    [Fact]
    public async Task RestoreRevisionAsync_ShouldThrowUnauthorized_WhenFileNotInSandbox()
    {
        // Arrange
        var settings = new AppSettings();
        var (service, httpHandler) = CreateServiceWithMockHttp(settings);
        using var context = McpContext.Symbolize(12345, 12345);

        httpHandler.Handler = req =>
        {
            var url = req.RequestUri?.ToString() ?? "";
            if (url.Contains("q=mimeType"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(@"{ ""files"": [ { ""id"": ""root_id"" } ] }") };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(@"{ ""files"": [] }") };
        };

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RestoreRevisionAsync(null, "unknown_file_id", "rev_1"));
    }
}
