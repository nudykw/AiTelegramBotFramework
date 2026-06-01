using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceLayer.Services.Mcp;
using ServiceLayer.Services.Mcp.GoogleDrive;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace ServiceLayer.UnitTests.Services.Mcp;

public class GoogleDriveMcpToolsTests
{
    private readonly Mock<IGoogleDriveService> _mockDriveService;

    public GoogleDriveMcpToolsTests()
    {
        _mockDriveService = new Mock<IGoogleDriveService>();
    }

    private GoogleDriveMcpTools CreateToolsInstance(string name)
    {
        return new GoogleDriveMcpTools(name, _mockDriveService.Object, NullLogger<GoogleDriveMcpTools>.Instance);
    }

    [Fact]
    public void ToolProperties_ShouldMatchSpecification()
    {
        var tool = CreateToolsInstance(GoogleDriveMcpTools.ToolList);
        Assert.Equal(GoogleDriveMcpTools.ToolList, tool.Name);
        Assert.Contains("Recursively", tool.Description);
        Assert.Contains("type", tool.JsonSchema);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnJsonList_WhenToolListCalled()
    {
        // Arrange
        var tool = CreateToolsInstance(GoogleDriveMcpTools.ToolList);
        var mockItems = new List<GoogleDriveItem>
        {
            new GoogleDriveItem { Id = "1", Name = "a.md", Type = "file", VirtualPath = "/a.md", Description = "Desc" }
        };
        _mockDriveService.Setup(s => s.ListAllAsync(null, It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync(mockItems);

        // Act
        var json = await tool.ExecuteAsync("{}");

        // Assert
        Assert.NotNull(json);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(JsonValueKind.Array, root.ValueKind);
        Assert.Equal(1, root.GetArrayLength());
        Assert.Equal("1", root[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnRawContent_WhenToolReadCalledWithValidArgs()
    {
        // Arrange
        var tool = CreateToolsInstance(GoogleDriveMcpTools.ToolRead);
        var mockItem = new GoogleDriveItem { Id = "file_id", Size = 100, Md5Checksum = "hash123", Description = "Note desc" };
        _mockDriveService.Setup(s => s.ReadFileContentAsync(null, "file_id")).ReturnsAsync(("Markdown content", mockItem));

        // Act
        var json = await tool.ExecuteAsync("{\"fileId\": \"file_id\"}");

        // Assert
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("file_id", root.GetProperty("fileId").GetString());
        Assert.Equal("Markdown content", root.GetProperty("content").GetString());
        Assert.Equal(100, root.GetProperty("size").GetInt64());
        Assert.Equal("hash123", root.GetProperty("md5Checksum").GetString());
        Assert.Equal("Note desc", root.GetProperty("description").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnError_WhenToolReadCalledWithMissingArgs()
    {
        // Arrange
        var tool = CreateToolsInstance(GoogleDriveMcpTools.ToolRead);

        // Act
        var json = await tool.ExecuteAsync("{}");

        // Assert
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("error", out var err));
        Assert.Contains("fileId", err.GetString() ?? "");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnSuccessJson_WhenToolWriteCalled()
    {
        // Arrange
        var tool = CreateToolsInstance(GoogleDriveMcpTools.ToolWrite);
        var mockItem = new GoogleDriveItem { Id = "file_id", Name = "a.md", Type = "file", VirtualPath = "/a.md" };
        _mockDriveService.Setup(s => s.WriteFileContentAsync(null, "file_id", "New Content")).ReturnsAsync(mockItem);

        // Act
        var json = await tool.ExecuteAsync("{\"fileId\": \"file_id\", \"content\": \"New Content\"}");

        // Assert
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("file_id", root.GetProperty("item").GetProperty("id").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnError_WhenToolWriteCalledWithMissingArgs()
    {
        // Arrange
        var tool = CreateToolsInstance(GoogleDriveMcpTools.ToolWrite);

        // Act & Assert (missing content)
        var json1 = await tool.ExecuteAsync("{\"fileId\": \"file_id\"}");
        using (var doc1 = JsonDocument.Parse(json1))
        {
            Assert.True(doc1.RootElement.TryGetProperty("error", out var err));
            Assert.Contains("content", err.GetString() ?? "");
        }

        // Act & Assert (missing fileId)
        var json2 = await tool.ExecuteAsync("{\"content\": \"a\"}");
        using (var doc2 = JsonDocument.Parse(json2))
        {
            Assert.True(doc2.RootElement.TryGetProperty("error", out var err));
            Assert.Contains("fileId", err.GetString() ?? "");
        }
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnCreatedItem_WhenToolCreateCalledWithValidArgs()
    {
        // Arrange
        var tool = CreateToolsInstance(GoogleDriveMcpTools.ToolCreate);
        var mockItem = new GoogleDriveItem { Id = "new_id", Name = "note.md", Type = "file", VirtualPath = "/note.md", Description = "Note description" };
        _mockDriveService.Setup(s => s.CreateItemAsync(null, "note.md", "file", "Note description", null)).ReturnsAsync(mockItem);

        // Act
        var json = await tool.ExecuteAsync("{\"name\": \"note.md\", \"type\": \"file\", \"description\": \"Note description\"}");

        // Assert
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("new_id", root.GetProperty("id").GetString());
        Assert.Equal("note.md", root.GetProperty("name").GetString());
        Assert.Equal("Note description", root.GetProperty("description").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnError_WhenToolCreateCalledWithNonMdFile()
    {
        // Arrange
        var tool = CreateToolsInstance(GoogleDriveMcpTools.ToolCreate);

        // Act
        var json = await tool.ExecuteAsync("{\"name\": \"note.txt\", \"type\": \"file\", \"description\": \"Note description\"}");

        // Assert
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("error", out var err));
        Assert.Contains("must end with '.md'", err.GetString() ?? "");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnError_WhenToolCreateCalledWithMissingArgs()
    {
        // Arrange
        var tool = CreateToolsInstance(GoogleDriveMcpTools.ToolCreate);

        // Act & Assert
        var json = await tool.ExecuteAsync("{\"name\": \"note.md\", \"type\": \"file\"}");
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("error", out var err));
        Assert.Contains("description", err.GetString() ?? "");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnUpdatedItem_WhenToolUpdateDescriptionCalled()
    {
        // Arrange
        var tool = CreateToolsInstance(GoogleDriveMcpTools.ToolUpdateDescription);
        var mockItem = new GoogleDriveItem { Id = "item_id", Name = "a.md", Type = "file", VirtualPath = "/a.md", Description = "New Desc" };
        _mockDriveService.Setup(s => s.UpdateDescriptionAsync(null, "item_id", "New Desc")).ReturnsAsync(mockItem);

        // Act
        var json = await tool.ExecuteAsync("{\"id\": \"item_id\", \"newDescription\": \"New Desc\"}");

        // Assert
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("item_id", root.GetProperty("id").GetString());
        Assert.Equal("New Desc", root.GetProperty("description").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnSuccessJson_WhenToolDeleteCalled()
    {
        // Arrange
        var tool = CreateToolsInstance(GoogleDriveMcpTools.ToolDelete);
        _mockDriveService.Setup(s => s.DeleteItemAsync(null, "file_id")).ReturnsAsync(true);

        // Act
        var json = await tool.ExecuteAsync("{\"id\": \"file_id\"}");

        // Assert
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("file_id", root.GetProperty("id").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnExceptionMessage_WhenServiceThrows()
    {
        // Arrange
        var tool = CreateToolsInstance(GoogleDriveMcpTools.ToolList);
        _mockDriveService.Setup(s => s.ListAllAsync(null, It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>())).ThrowsAsync(new InvalidOperationException("API Quota Exceeded"));

        // Act
        var json = await tool.ExecuteAsync("{}");

        // Assert
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("error", out var err));
        Assert.Contains("API Quota Exceeded", err.GetString() ?? "");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnSuccessJson_WhenToolRestoreCalled()
    {
        // Arrange
        var tool = CreateToolsInstance(GoogleDriveMcpTools.ToolRestore);
        var mockItem = new GoogleDriveItem { Id = "file_id", Name = "a.md", Type = "file", VirtualPath = "/a.md", IsTrashed = false };
        _mockDriveService.Setup(s => s.RestoreItemAsync(null, "file_id")).ReturnsAsync(mockItem);

        // Act
        var json = await tool.ExecuteAsync("{\"id\": \"file_id\"}");

        // Assert
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("file_id", root.GetProperty("item").GetProperty("id").GetString());
        Assert.False(root.GetProperty("item").GetProperty("isTrashed").GetBoolean());
    }

    // ── gdrive_list_versions ─────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ShouldReturnRevisionList_WhenToolListVersionsCalled()
    {
        // Arrange
        var tool = CreateToolsInstance(GoogleDriveMcpTools.ToolListVersions);
        var mockRevisions = new List<GoogleDriveRevision>
        {
            new() { Id = "rev_1", ModifiedTime = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), Size = 100, KeepForever = false },
            new() { Id = "rev_2", ModifiedTime = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc), Size = 200, KeepForever = true }
        };
        _mockDriveService
            .Setup(s => s.ListRevisionsAsync(null, "file_id"))
            .ReturnsAsync(mockRevisions);

        // Act
        var json = await tool.ExecuteAsync("{\"fileId\": \"file_id\"}");

        // Assert
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("file_id", root.GetProperty("fileId").GetString());
        var revisions = root.GetProperty("revisions");
        Assert.Equal(JsonValueKind.Array, revisions.ValueKind);
        Assert.Equal(2, revisions.GetArrayLength());
        Assert.Equal("rev_1", revisions[0].GetProperty("id").GetString());
        Assert.Equal("rev_2", revisions[1].GetProperty("id").GetString());
        Assert.True(revisions[1].GetProperty("keepForever").GetBoolean());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnError_WhenToolListVersionsCalledWithoutFileId()
    {
        // Arrange
        var tool = CreateToolsInstance(GoogleDriveMcpTools.ToolListVersions);

        // Act
        var json = await tool.ExecuteAsync("{}");

        // Assert
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("error", out var err));
        Assert.Contains("fileId", err.GetString() ?? "");
    }

    // ── gdrive_read_version ──────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ShouldReturnRevisionContent_WhenToolReadVersionCalledWithValidArgs()
    {
        // Arrange
        var tool = CreateToolsInstance(GoogleDriveMcpTools.ToolReadVersion);
        var mockRevision = new GoogleDriveRevision
        {
            Id = "rev_1",
            ModifiedTime = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Size = 42,
            LastModifyingUserEmail = "user@example.com"
        };
        _mockDriveService
            .Setup(s => s.ReadRevisionContentAsync(null, "file_id", "rev_1"))
            .ReturnsAsync(("# Old content", mockRevision));

        // Act
        var json = await tool.ExecuteAsync("{\"fileId\": \"file_id\", \"revisionId\": \"rev_1\"}");

        // Assert
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("file_id", root.GetProperty("fileId").GetString());
        Assert.Equal("rev_1", root.GetProperty("revisionId").GetString());
        Assert.Equal("# Old content", root.GetProperty("content").GetString());
        Assert.Equal(42, root.GetProperty("size").GetInt64());
        Assert.Equal("user@example.com", root.GetProperty("lastModifyingUserEmail").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnError_WhenToolReadVersionMissingFileId()
    {
        // Arrange
        var tool = CreateToolsInstance(GoogleDriveMcpTools.ToolReadVersion);

        // Act
        var json = await tool.ExecuteAsync("{\"revisionId\": \"rev_1\"}");

        // Assert
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("error", out var err));
        Assert.Contains("fileId", err.GetString() ?? "");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnError_WhenToolReadVersionMissingRevisionId()
    {
        // Arrange
        var tool = CreateToolsInstance(GoogleDriveMcpTools.ToolReadVersion);

        // Act
        var json = await tool.ExecuteAsync("{\"fileId\": \"file_id\"}");

        // Assert
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("error", out var err));
        Assert.Contains("revisionId", err.GetString() ?? "");
    }

    // ── gdrive_restore_version ───────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ShouldReturnSuccess_WhenToolRestoreVersionCalledWithValidArgs()
    {
        // Arrange
        var tool = CreateToolsInstance(GoogleDriveMcpTools.ToolRestoreVersion);
        var mockItem = new GoogleDriveItem { Id = "file_id", Name = "a.md", Type = "file", VirtualPath = "/a.md" };
        _mockDriveService
            .Setup(s => s.RestoreRevisionAsync(null, "file_id", "rev_1", false))
            .ReturnsAsync((mockItem, "rev_3"));

        // Act
        var json = await tool.ExecuteAsync("{\"fileId\": \"file_id\", \"revisionId\": \"rev_1\"}");

        // Assert
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("file_id", root.GetProperty("fileId").GetString());
        Assert.Equal("rev_1", root.GetProperty("restoredFromRevisionId").GetString());
        Assert.Equal("rev_3", root.GetProperty("newRevisionId").GetString());
        Assert.Equal("file_id", root.GetProperty("item").GetProperty("id").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldPassKeepForeverTrue_WhenRestoreVersionCalledWithKeepForever()
    {
        // Arrange
        var tool = CreateToolsInstance(GoogleDriveMcpTools.ToolRestoreVersion);
        var mockItem = new GoogleDriveItem { Id = "file_id", Name = "a.md", Type = "file", VirtualPath = "/a.md" };
        _mockDriveService
            .Setup(s => s.RestoreRevisionAsync(null, "file_id", "rev_1", true))
            .ReturnsAsync((mockItem, "rev_3"));

        // Act
        var json = await tool.ExecuteAsync("{\"fileId\": \"file_id\", \"revisionId\": \"rev_1\", \"keepForever\": true}");

        // Assert
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        _mockDriveService.Verify(s => s.RestoreRevisionAsync(null, "file_id", "rev_1", true), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnError_WhenToolRestoreVersionMissingRevisionId()
    {
        // Arrange
        var tool = CreateToolsInstance(GoogleDriveMcpTools.ToolRestoreVersion);

        // Act
        var json = await tool.ExecuteAsync("{\"fileId\": \"file_id\"}");

        // Assert
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("error", out var err));
        Assert.Contains("revisionId", err.GetString() ?? "");
    }
}
