using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceLayer.Services;
using ServiceLayer.Services.Mcp;
using ServiceLayer.Services.Mcp.GoogleDrive;
using Xunit;

namespace ServiceLayer.UnitTests.Services.Mcp
{
    public class SshMcpToolsTests : IDisposable
    {
        private readonly Mock<IGoogleDriveService> _mockDriveService;

        public SshMcpToolsTests()
        {
            _mockDriveService = new Mock<IGoogleDriveService>();
        }

        public void Dispose()
        {
            // Clear McpContext after each test
            // Symbolize with null or 0 to clear
        }

        private SshMcpTools CreateToolsInstance(long? ownerId = 12345)
        {
            var appSettings = new AppSettings
            {
                TelegramBotConfiguration = new ServiceLayer.Services.Telegram.Configuretions.TelegramBotConfiguration
                {
                    OwnerId = ownerId
                }
            };
            return new SshMcpTools(SshMcpTools.ToolExecute, _mockDriveService.Object, appSettings, NullLogger<SshMcpTools>.Instance);
        }

        [Fact]
        public void ToolProperties_ShouldMatchSpecification()
        {
            var tool = CreateToolsInstance();
            Assert.Equal(SshMcpTools.ToolExecute, tool.Name);
            Assert.Equal("native-ssh", tool.ServerName);
            Assert.Contains("SSH", tool.Description);
            Assert.Contains("host", tool.JsonSchema);
            Assert.Contains("keyFileId", tool.JsonSchema);
        }

        [Fact]
        public async Task ExecuteAsync_ShouldReturnError_WhenUserIdContextIsMissing()
        {
            // Arrange
            var tool = CreateToolsInstance();

            // Act
            var result = await tool.ExecuteAsync("{}");

            // Assert
            Assert.Contains("Could not determine current user context", result);
        }

        [Fact]
        public async Task ExecuteAsync_ShouldReturnError_WhenArgumentsAreInvalidJson()
        {
            // Arrange
            var tool = CreateToolsInstance();
            using var context = McpContext.Symbolize(12345, 12345);

            // Act
            var result = await tool.ExecuteAsync("invalid-json");

            // Assert
            Assert.Contains("Invalid arguments JSON", result);
        }

        [Fact]
        public async Task ExecuteAsync_ShouldReturnError_WhenRequiredParametersAreMissing()
        {
            // Arrange
            var tool = CreateToolsInstance();
            using var context = McpContext.Symbolize(12345, 12345);

            // Act (missing command and host)
            var result = await tool.ExecuteAsync("{\"username\":\"mcp-agent\",\"keyFileId\":\"key_id\"}");

            // Assert
            Assert.Contains("required parameters", result);
        }

        [Fact]
        public async Task ExecuteAsync_ShouldReturnError_WhenGoogleDriveServiceFails()
        {
            // Arrange
            var tool = CreateToolsInstance();
            using var context = McpContext.Symbolize(12345, 12345);

            _mockDriveService
                .Setup(s => s.ReadFileContentAsync("12345", "key_id"))
                .ThrowsAsync(new Exception("File not found or unauthorized"));

            var args = new
            {
                host = "192.168.88.27",
                username = "mcp-agent",
                keyFileId = "key_id",
                command = "uname -a"
            };
            var argsJson = JsonSerializer.Serialize(args);

            // Act
            var result = await tool.ExecuteAsync(argsJson);

            // Assert
            Assert.Contains("Failed to fetch private key from Google Drive", result);
            Assert.Contains("File not found or unauthorized", result);
        }

        [Fact]
        public async Task ExecuteAsync_ShouldReturnError_WhenPrivateKeyIsEmpty()
        {
            // Arrange
            var tool = CreateToolsInstance();
            using var context = McpContext.Symbolize(12345, 12345);

            _mockDriveService
                .Setup(s => s.ReadFileContentAsync("12345", "key_id"))
                .ReturnsAsync(("", new GoogleDriveItem()));

            var args = new
            {
                host = "192.168.88.27",
                username = "mcp-agent",
                keyFileId = "key_id",
                command = "uname -a"
            };
            var argsJson = JsonSerializer.Serialize(args);

            // Act
            var result = await tool.ExecuteAsync(argsJson);

            // Assert
            Assert.Contains("private key returned empty content", result);
        }

        [Fact]
        public async Task ExecuteAsync_ShouldReturnError_WhenConnectionFailsWithInvalidHost()
        {
            // Arrange
            var tool = CreateToolsInstance();
            using var context = McpContext.Symbolize(12345, 12345);

            const string mockKey = "-----BEGIN OPENSSH PRIVATE KEY-----\nb3BlbnNzaC1rZXktdjEAAAAABG5vbmUAAAAEbm9uZQAAAAAAAAABAAAAMwAAAAtzc2gtZW\n-----END OPENSSH PRIVATE KEY-----";

            _mockDriveService
                .Setup(s => s.ReadFileContentAsync("12345", "key_id"))
                .ReturnsAsync((mockKey, new GoogleDriveItem()));

            var args = new
            {
                host = "invalid.local.ip.address",
                username = "mcp-agent",
                keyFileId = "key_id",
                command = "uname -a",
                timeout = 100 // extremely short timeout to fail instantly
            };
            var argsJson = JsonSerializer.Serialize(args);

            // Act
            var result = await tool.ExecuteAsync(argsJson);

            // Assert
            Assert.Contains("SSH operation failed", result);
        }

        [Fact]
        public async Task ExecuteAsync_ShouldReturnError_WhenUserIsNotTheOwner()
        {
            // Arrange
            var tool = CreateToolsInstance(ownerId: 99999);
            using var context = McpContext.Symbolize(12345, 12345);

            // Act
            var result = await tool.ExecuteAsync("{}");

            // Assert
            Assert.Contains("Access Denied. The SSH MCP tool can only be executed by the bot owner", result);
        }
    }
}
