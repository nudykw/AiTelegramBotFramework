using DataBaseLayer.Models;
using DataBaseLayer.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using ServiceLayer.Services;
using ServiceLayer.Services.Mcp.GoogleDrive;

namespace ServiceLayer.Services.Mcp;

public class McpServerManager
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<McpServerManager> _logger;
    private readonly AppSettings _settings;
    private readonly ConcurrentDictionary<long, McpClient> _clients = new();
    private readonly ConcurrentDictionary<string, long> _toolToClientId = new();

    public McpServerManager(IServiceProvider serviceProvider, ILogger<McpServerManager> logger, AppSettings settings)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _settings = settings;
    }

    public virtual async Task ReloadServersAsync()
    {
        // Clear cached Google Drive credentials so new files/configurations are reread
        using (var scope = _serviceProvider.CreateScope())
        {
            var authService = scope.ServiceProvider.GetService<IGoogleDriveAuthService>();
            if (authService != null)
            {
                try
                {
                    authService.ResetCache();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to reset Google Drive authentication cache.");
                }
            }
        }

        await SeedToolsAsync();
        _logger.LogInformation("Reloading MCP servers...");
        
        foreach (var client in _clients.Values)
        {
            try { await client.DisposeAsync(); } catch { }
        }
        _clients.Clear();
        _toolToClientId.Clear();

        using var scope2 = _serviceProvider.CreateScope();
        var repo = scope2.ServiceProvider.GetRequiredService<IRepository<McpToolRecord>>();
        var activeTools = repo.GetAll().Where(t => t.IsActive).ToList();

        foreach (var tool in activeTools)
        {
            try
            {
                _logger.LogInformation("Starting MCP tool: {Name}", tool.Name);
                
                var args = JsonSerializer.Deserialize<string[]>(tool.Args) ?? Array.Empty<string>();
                var env = JsonSerializer.Deserialize<Dictionary<string, string?>>(tool.EnvVars) ?? new();

                var allowedCommands = new[] { "npx", "node", "docker" };
                if (string.IsNullOrWhiteSpace(tool.Command) || !allowedCommands.Contains(tool.Command))
                {
                    _logger.LogError("🚨 Invalid command '{Command}' for MCP tool '{Name}'. Only 'npx', 'node', and 'docker' are allowed.", tool.Command, tool.Name);
                    continue;
                }

                var runtimeContainer = Environment.GetEnvironmentVariable("MCP_RUNTIME_CONTAINER");
                var command = tool.Command;
                var finalArgs = args;

                if (!string.IsNullOrEmpty(runtimeContainer))
                {
                    _logger.LogInformation("Running MCP tool {Name} in container {Container}", tool.Name, runtimeContainer);
                    command = "docker";
                    var dockerArgs = new List<string> { "exec", "-i" };
                    
                    // Pass environment variables to the container
                    foreach (var kvp in env)
                    {
                        if (kvp.Value != null)
                        {
                            dockerArgs.Add("-e");
                            dockerArgs.Add($"{kvp.Key}={kvp.Value}");
                        }
                    }
                    
                    dockerArgs.Add(runtimeContainer);
                    dockerArgs.Add(tool.Command);
                    dockerArgs.AddRange(args);
                    finalArgs = dockerArgs.ToArray();
                }

                var transportOptions = new StdioClientTransportOptions
                {
                    Command = command,
                    Arguments = finalArgs,
                    EnvironmentVariables = env
                };

                _logger.LogInformation("Starting MCP transport for {Name}: {Command} {Args}", tool.Name, command, string.Join(" ", finalArgs));

                var transport = new StdioClientTransport(transportOptions);
                
                // Use factory method to create the client
                var client = await McpClient.CreateAsync(transport);
                
                _clients[tool.Id] = client;

                // Map tools to this client
                try
                {
                    var tools = await client.ListToolsAsync();
                    if (tools != null)
                    {
                        foreach (var t in tools)
                        {
                            _toolToClientId[t.Name] = tool.Id;
                        }
                        _logger.LogInformation("✅ [HEALTH CHECK PASSED] MCP tool: {Name} ({Count} tools found)", tool.Name, tools.Count);
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ [HEALTH CHECK FAILED] MCP tool: {Name} (Returned empty tool list)", tool.Name);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "❌ [HEALTH CHECK FAILED] MCP tool: {Name}. Connection error: {Message}", tool.Name, ex.Message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "🚨 [FATAL ERROR] Failed to start MCP tool process: {Name}", tool.Name);
            }
        }
    }

    public virtual async Task<List<McpClientTool>> GetAllToolsAsync()
    {
        var allTools = new List<McpClientTool>();
        
        foreach (var client in _clients.Values)
        {
            try
            {
                var tools = await client.ListToolsAsync();
                if (tools != null)
                {
                    allTools.AddRange(tools);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to list tools from one of the MCP clients.");
            }
        }

        // Add native tools dynamically
        try
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var nativeTools = scope.ServiceProvider.GetServices<INativeMcpTool>();
                bool? isGoogleDriveAvailable = null;

                foreach (var nativeTool in nativeTools)
                {
                    try
                    {
                        if (nativeTool is GoogleDriveMcpTools)
                        {
                            if (isGoogleDriveAvailable == null)
                            {
                                var authService = scope.ServiceProvider.GetService<IGoogleDriveAuthService>();
                                if (authService == null)
                                {
                                    _logger.LogWarning("⚠️ Google Drive MCP tools will not start because the authentication service is not registered in DI.");
                                    isGoogleDriveAvailable = false;
                                }
                                else
                                {
                                    try
                                    {
                                        await authService.GetDriveServiceAsync();
                                        isGoogleDriveAvailable = true;
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogWarning("⚠️ Google Drive MCP tools will not start. Authentication failed: {Message}", ex.Message);
                                        isGoogleDriveAvailable = false;
                                    }
                                }
                            }

                            if (isGoogleDriveAvailable == false)
                            {
                                // Skip registering Google Drive tools
                                continue;
                            }
                        }

                        if (nativeTool is SshMcpTools)
                        {
                            var appSettings = scope.ServiceProvider.GetService<AppSettings>();
                            var ownerId = appSettings?.TelegramBotConfiguration?.OwnerId;
                            var currentUserId = McpContext.UserId;

                            if (ownerId == null || currentUserId == null || currentUserId.Value != ownerId.Value)
                            {
                                // Skip registering SSH tool if the current user is not the owner
                                continue;
                            }
                        }

                        var clientTool = CreateMcpClientTool(nativeTool);
                        allTools.Add(clientTool);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to wrap native tool {Name}", nativeTool.Name);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve native tools from DI");
        }
        
        return allTools;
    }

    public virtual async Task<string> ExecuteToolAsync(string toolName, string argumentsJson)
    {
        _logger.LogInformation("Executing MCP tool: {Name} with args: {Args}", toolName, argumentsJson);
        
        // 1. Check if it is a native C# tool first
        try
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var nativeTools = scope.ServiceProvider.GetServices<INativeMcpTool>();
                var nativeTool = nativeTools.FirstOrDefault(t => t.Name == toolName);
                if (nativeTool != null)
                {
                    try
                    {
                        if (nativeTool is GoogleDriveMcpTools)
                        {
                            var authService = scope.ServiceProvider.GetService<IGoogleDriveAuthService>();
                            if (authService == null)
                            {
                                return "Error: Google Drive authentication service is not registered.";
                            }
                            await authService.GetDriveServiceAsync(); // will throw if invalid/missing
                        }

                        return await nativeTool.ExecuteAsync(argumentsJson);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error executing native MCP tool {Name}", toolName);
                        return $"Error: {ex.Message}";
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve native tools during execution of {Name}", toolName);
        }

        // 2. Fall back to external client tools
        if (_toolToClientId.TryGetValue(toolName, out var clientId) && _clients.TryGetValue(clientId, out var client))
        {
            try
            {
                var args = JsonSerializer.Deserialize<Dictionary<string, object?>>(argumentsJson);
                var result = await client.CallToolAsync(toolName, args);
                if (result == null) return "Error: Tool returned no result.";

                var json = JsonSerializer.Serialize(result);
                using var doc = JsonDocument.Parse(json);
                
                bool isError = doc.RootElement.TryGetProperty("isError", out var isErrorProp) && isErrorProp.ValueKind == JsonValueKind.True;
                if (isError)
                {
                    return json;
                }

                if (doc.RootElement.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                {
                    var texts = new List<string>();
                    foreach (var block in content.EnumerateArray())
                    {
                        if (block.TryGetProperty("type", out var type) && type.GetString() == "text" && block.TryGetProperty("text", out var text))
                        {
                            var textVal = text.GetString();
                            if (!string.IsNullOrEmpty(textVal))
                            {
                                texts.Add(textVal);
                            }
                        }
                    }
                    
                    if (texts.Any())
                    {
                        return string.Join("\n\n", texts);
                    }
                }

                return json;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing MCP tool {Name}", toolName);
                return $"Error: {ex.Message}";
            }
        }
        
        return $"Error: Tool '{toolName}' not found among active MCP servers.";
    }

    public virtual string? GetServerNameForTool(string toolName)
    {
        using var scope = _serviceProvider.CreateScope();

        // 1. Check native (C#) tools first — they carry ServerName directly
        var nativeTools = scope.ServiceProvider.GetServices<INativeMcpTool>();
        var native = nativeTools.FirstOrDefault(t => t.Name == toolName);
        if (native != null)
            return native.ServerName;

        // 2. Fall back to external (DB-stored) MCP server records
        if (_toolToClientId.TryGetValue(toolName, out var clientId))
        {
            var repo = scope.ServiceProvider.GetRequiredService<IRepository<McpToolRecord>>();
            var record = repo.GetAll().FirstOrDefault(r => r.Id == clientId);
            return record?.Name;
        }

        return null;
    }

    private McpClientTool CreateMcpClientTool(INativeMcpTool nativeTool)
    {
        var tool = new Tool
        {
            Name = nativeTool.Name,
            Description = nativeTool.Description,
            InputSchema = JsonDocument.Parse(nativeTool.JsonSchema).RootElement
        };

        var coreAssembly = Assembly.Load("ModelContextProtocol.Core");
        var implType = coreAssembly.GetTypes().FirstOrDefault(t => t.Name == "McpClientImpl")
            ?? throw new InvalidOperationException("McpClientImpl type not found.");

        var transport = new FakeTransport();
        var options = new McpClientOptions();
        var loggerFactory = NullLoggerFactory.Instance;

        var dummyClient = (McpClient)Activator.CreateInstance(
            implType, 
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance, 
            null, 
            new object[] { transport, "native-client", options, loggerFactory }, 
            null)!;

        return new McpClientTool(dummyClient, tool, new JsonSerializerOptions());
    }

    public virtual async Task SeedToolsAsync()
    {
        if (_settings?.McpSettings?.DefaultTools == null || !_settings.McpSettings.DefaultTools.Any())
        {
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<McpToolRecord>>();
        var existingTools = repo.GetAll().ToList();

        // 1. Remove exact duplicates already in DB (same Command and Args)
        var groups = existingTools.GroupBy(t => new { t.Command, t.Args }).Where(g => g.Count() > 1);
        foreach (var group in groups)
        {
            var toDelete = group.Skip(1);
            foreach (var item in toDelete)
            {
                _logger.LogInformation("Removing duplicate MCP tool: {Name} (ID: {Id})", item.Name, item.Id);
                repo.Delete(item);
            }
        }
        if (groups.Any()) await repo.SaveChanges();

        // 2. Add new tools from config if they don't exist by Command and Args
        var updatedExisting = repo.GetAll().ToList();
        foreach (var defaultTool in _settings.McpSettings.DefaultTools)
        {
            // Check by Command and Args instead of Name, because Name can change but logic is the same
            if (!updatedExisting.Any(t => t.Command == defaultTool.Command && t.Args == defaultTool.Args))
            {
                _logger.LogInformation("Seeding default MCP tool: {Name}", defaultTool.Name);
                repo.Add(new McpToolRecord
                {
                    Name = defaultTool.Name,
                    Command = defaultTool.Command,
                    Args = defaultTool.Args,
                    EnvVars = defaultTool.EnvVars,
                    IsActive = true
                });
            }
        }
        await repo.SaveChanges();
    }
}
