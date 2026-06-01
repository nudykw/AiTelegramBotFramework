using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using DataBaseLayer.Repositories;
using DataBaseLayer.Models;
using ServiceLayer.Services.Mcp;
using TelegramBotWebApp.Tests.Fixtures;

namespace TelegramBotWebApp.Tests.Endpoints;

/// <summary>
/// Integration tests for MCP Files Management RESTful API endpoints.
/// Verifies Upload (POST), List (GET), Delete (DELETE) operations, and ensures raw download is disabled.
/// </summary>
public class McpFilesEndpointsTests : IClassFixture<WebAppFactory>
{
    private readonly HttpClient _client;
    private readonly WebAppFactory _factory;

    public McpFilesEndpointsTests(WebAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        CleanUploadsDirectory();
    }

    private void CleanUploadsDirectory()
    {
        using var scope = _factory.Services.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
        var dir = config["AppSettings:Mcp:UploadsDir"] ?? Environment.GetEnvironmentVariable("MCP_UPLOADS_DIR");
        if (dir != null && Directory.Exists(dir))
        {
            foreach (var file in Directory.GetFiles(dir))
            {
                try { File.Delete(file); } catch { }
            }
        }
    }

    [Fact]
    public async Task McpFiles_FullLifeCycle_Upload_List_Delete_VerifyNoDownload()
    {
        // 1. GET /admin/mcp/files - List should initially be empty
        var getInitialResponse = await _client.GetAsync("/admin/mcp/files");
        Assert.Equal(HttpStatusCode.OK, getInitialResponse.StatusCode);
        var getInitialHtml = await getInitialResponse.Content.ReadAsStringAsync();
        Assert.Contains("No files uploaded yet", getInitialHtml);

        // 2. POST /admin/mcp/file - Upload a fake credentials file
        const string filename = "test_integration_secret.json";
        const string fileContent = "{\"client_email\": \"test@fenix-drive.iam.gserviceaccount.com\"}";

        using var content = new MultipartFormDataContent();
        var fileContentBytes = new ByteArrayContent(Encoding.UTF8.GetBytes(fileContent));
        fileContentBytes.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
        content.Add(fileContentBytes, "file", filename);

        var uploadResponse = await _client.PostAsync("/admin/mcp/file", content);
        // Should redirect (302 Found) back to /admin/mcp/files after successful upload
        Assert.Equal(HttpStatusCode.Found, uploadResponse.StatusCode);
        Assert.Equal("/admin/mcp/files", uploadResponse.Headers.Location?.OriginalString);

        // 3. GET /admin/mcp/files - Verify the file is listed
        var getListedResponse = await _client.GetAsync("/admin/mcp/files");
        Assert.Equal(HttpStatusCode.OK, getListedResponse.StatusCode);
        var getListedHtml = await getListedResponse.Content.ReadAsStringAsync();
        Assert.Contains(filename, getListedHtml);
        Assert.Contains($"/app/mcp-uploads/{filename}", getListedHtml);
        Assert.DoesNotContain("No files uploaded yet", getListedHtml);

        // 4. Verify that raw download is strictly forbidden (there shouldn't be a GET endpoint returning the file content)
        var downloadResponse = await _client.GetAsync($"/admin/mcp/file/{filename}");
        // It should either return 404 (Not Found) or 405 (Method Not Allowed) because we don't map GET /admin/mcp/file/{filename}
        Assert.True(downloadResponse.StatusCode == HttpStatusCode.NotFound || downloadResponse.StatusCode == HttpStatusCode.MethodNotAllowed);

        // 5. DELETE /admin/mcp/file/{filename} - Delete the uploaded file
        var deleteResponse = await _client.DeleteAsync($"/admin/mcp/file/{filename}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        // 6. GET /admin/mcp/files - Verify the list is empty again
        var getFinalResponse = await _client.GetAsync("/admin/mcp/files");
        Assert.Equal(HttpStatusCode.OK, getFinalResponse.StatusCode);
        var getFinalHtml = await getFinalResponse.Content.ReadAsStringAsync();
        Assert.Contains("No files uploaded yet", getFinalHtml);
        Assert.DoesNotContain(filename, getFinalHtml);
    }

    [Fact]
    public async Task McpFiles_FakeMcpReadsEnvFile_Succeeds()
    {
        using var scope = _factory.Services.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
        var testUploadsDir = config["AppSettings:Mcp:UploadsDir"] ?? Environment.GetEnvironmentVariable("MCP_UPLOADS_DIR");
        Assert.NotNull(testUploadsDir);
        if (!Directory.Exists(testUploadsDir))
        {
            Directory.CreateDirectory(testUploadsDir);
        }

        // 1. Write the inline JavaScript MCP server to a file
        var scriptPath = Path.Combine(testUploadsDir, "fake-mcp-server.js");
        var mcpScript = """
const fs = require('fs');
const readline = require('readline');

const rl = readline.createInterface({
    input: process.stdin,
    output: process.stdout,
    terminal: false
});

rl.on('line', (line) => {
    try {
        const req = JSON.parse(line);
        if (req.method === 'initialize') {
            console.log(JSON.stringify({
                jsonrpc: '2.0',
                id: req.id,
                result: {
                    protocolVersion: '2024-11-05',
                    capabilities: {},
                    serverInfo: { name: 'fake-mcp', version: '1.0' }
                }
            }));
        } else if (req.method === 'tools/list') {
            console.log(JSON.stringify({
                jsonrpc: '2.0',
                id: req.id,
                result: {
                    tools: [{
                        name: 'read_env_file',
                        description: 'Reads env file',
                        inputSchema: { type: 'object', properties: {} }
                    }]
                }
            }));
        } else if (req.method === 'tools/call') {
            const filePath = process.env.TEST_FILE_PATH;
            let fileContent = 'No path in env';
            if (filePath && fs.existsSync(filePath)) {
                fileContent = fs.readFileSync(filePath, 'utf8');
            } else {
                fileContent = 'File not found';
            }
            console.log(JSON.stringify({
                jsonrpc: '2.0',
                id: req.id,
                result: {
                    content: [{ type: 'text', text: fileContent }],
                    isError: false
                }
            }));
        }
    } catch (e) {
        // ignore
    }
});
""";
        await File.WriteAllTextAsync(scriptPath, mcpScript);

        // 2. Create the test credentials file
        var credentialsFileName = "gdrive-creds-test.json";
        var credentialsFilePath = Path.Combine(testUploadsDir, credentialsFileName);
        const string expectedContent = "{\"private_key\": \"my-super-secret-key-12345\"}";
        await File.WriteAllTextAsync(credentialsFilePath, expectedContent);

        // 3. Register our fake MCP tool in the test SQLite DB
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<McpToolRecord>>();
        
        var mcpTool = new McpToolRecord
        {
            Name = "GDriveFakeMcp",
            Command = "node",
            Args = JsonSerializer.Serialize(new[] { scriptPath }),
            EnvVars = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["TEST_FILE_PATH"] = credentialsFilePath
            }),
            IsActive = true
        };
        repo.Add(mcpTool);
        await repo.SaveChanges();

        // 4. Reload servers in McpServerManager
        var mcpManager = scope.ServiceProvider.GetRequiredService<McpServerManager>();
        await mcpManager.ReloadServersAsync();

        // 5. Execute the mock tool and assert that it successfully read our file content via environment variables!
        var toolResult = await mcpManager.ExecuteToolAsync("read_env_file", "{}");
        Assert.Equal(expectedContent, toolResult);

        // 6. Cleanup the tool from DB so other tests aren't polluted
        repo.Delete(mcpTool);
        await repo.SaveChanges();
    }
}
