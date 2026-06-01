using Microsoft.Extensions.DependencyInjection;
using ServiceLayer.Services;
using ServiceLayer.Services.Mcp;
using TelegramBotWebApp.Tests.Fixtures;
using Xunit;
using DataBaseLayer.Models;
using DataBaseLayer.Repositories;

namespace TelegramBotWebApp.Tests.Mcp;

public class McpConnectivityTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public McpConnectivityTests(WebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AllConfiguredMcpServers_ShouldBeConnectable()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var mcpManager = scope.ServiceProvider.GetRequiredService<McpServerManager>();

        // Act
        // Seed and reload to ensure tools from appsettings.json are in DB and connected
        await mcpManager.ReloadServersAsync();
        
        // Wait a bit for background connections if any (though ReloadServersAsync is awaited)
        var tools = await mcpManager.GetAllToolsAsync();

        // Assert
        // Check if any tools were loaded. If the servers didn't start, this will be empty.
        Assert.NotEmpty(tools);
        
        // Verify that each default tool from config is actually in the DB and loaded
        var settings = scope.ServiceProvider.GetRequiredService<AppSettings>();
        var expectedToolNames = settings.McpSettings.DefaultTools.Select(t => t.Name).ToList();

        using var dbScope = _factory.Services.CreateScope();
        var repo = dbScope.ServiceProvider.GetRequiredService<IRepository<McpToolRecord>>();
        var dbTools = repo.GetAll().ToList();
        
        foreach (var name in expectedToolNames)
        {
            Assert.Contains(dbTools, t => t.Name == name && t.IsActive);
        }
        
        // Optional: Log found tools for debugging
        foreach (var tool in tools)
        {
            // _factory.Output.WriteLine($"Found tool: {tool.Name}");
        }
    }
}
