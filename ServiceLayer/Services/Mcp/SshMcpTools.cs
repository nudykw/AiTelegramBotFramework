using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Renci.SshNet;
using ServiceLayer.Services.Mcp.GoogleDrive;

namespace ServiceLayer.Services.Mcp
{
    public class SshMcpTools : INativeMcpTool
    {
        private readonly IGoogleDriveService _driveService;
        private readonly ILogger<SshMcpTools> _logger;
        private readonly AppSettings _appSettings;
        private readonly string _toolName;

        public const string ToolExecute = "ssh_execute";

        public string Name => _toolName;
        public string ServerName => "native-ssh";

        public string Description => _toolName switch
        {
            ToolExecute => "Executes a shell command on a remote Linux server via SSH using a private key file fetched dynamically in-memory from the user's sandboxed Google Drive. Connections are 100% isolated and keys never touch the local disk. SECURITY WARNING: Only permitted for use by the bot owner.",
            _ => string.Empty
        };

        public string JsonSchema => _toolName switch
        {
            ToolExecute => """
                {
                  "type": "object",
                  "properties": {
                    "host": {
                      "type": "string",
                      "description": "Remote host IP address or domain name (e.g. 192.168.88.27)"
                    },
                    "port": {
                      "type": "integer",
                      "description": "SSH port (default is 22)",
                      "default": 22
                    },
                    "username": {
                      "type": "string",
                      "description": "SSH username (e.g. mcp-agent)"
                    },
                    "keyFileId": {
                      "type": "string",
                      "description": "Google Drive file ID of the private key file (e.g. id_nixos_mcp)"
                    },
                    "command": {
                      "type": "string",
                      "description": "Shell command to run on the remote host (e.g. uname -a)"
                    },
                    "timeout": {
                      "type": "integer",
                      "description": "Command execution timeout in milliseconds (default is 30000)",
                      "default": 30000
                    }
                  },
                  "required": ["host", "username", "keyFileId", "command"]
                }
                """,
            _ => "{\"type\":\"object\",\"properties\":{}}"
        };

        public SshMcpTools(
            string toolName,
            IGoogleDriveService driveService,
            AppSettings appSettings,
            ILogger<SshMcpTools> logger)
        {
            _toolName = toolName;
            _driveService = driveService;
            _appSettings = appSettings;
            _logger = logger;
        }

        public async Task<string> ExecuteAsync(string argumentsJson)
        {
            return _toolName switch
            {
                ToolExecute => await ExecuteSshCommandAsync(argumentsJson),
                _ => $"Error: Unknown tool '{_toolName}'."
            };
        }

        private async Task<string> ExecuteSshCommandAsync(string argumentsJson)
        {
            var userId = McpContext.UserId;
            if (userId == null)
            {
                return "Error: Could not determine current user context (UserId is missing).";
            }

            var ownerId = _appSettings?.TelegramBotConfiguration?.OwnerId;
            if (ownerId == null || userId.Value != ownerId.Value)
            {
                _logger.LogWarning("Security Alert: Non-owner user '{UserId}' attempted to execute SSH command.", userId);
                return "Error: Access Denied. The SSH MCP tool can only be executed by the bot owner.";
            }

            string host = string.Empty;
            int port = 22;
            string username = string.Empty;
            string keyFileId = string.Empty;
            string command = string.Empty;
            int timeout = 30000;

            try
            {
                using var doc = JsonDocument.Parse(argumentsJson);
                var root = doc.RootElement;

                if (root.TryGetProperty("host", out var hostVal))
                {
                    host = hostVal.GetString() ?? string.Empty;
                }
                if (root.TryGetProperty("username", out var usernameVal))
                {
                    username = usernameVal.GetString() ?? string.Empty;
                }
                if (root.TryGetProperty("keyFileId", out var keyFileIdVal))
                {
                    keyFileId = keyFileIdVal.GetString() ?? string.Empty;
                }
                if (root.TryGetProperty("command", out var commandVal))
                {
                    command = commandVal.GetString() ?? string.Empty;
                }
                if (root.TryGetProperty("port", out var portVal))
                {
                    port = portVal.GetInt32();
                }
                if (root.TryGetProperty("timeout", out var timeoutVal))
                {
                    timeout = timeoutVal.GetInt32();
                }
            }
            catch (Exception ex)
            {
                return $"Error: Invalid arguments JSON — {ex.Message}";
            }

            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(username) || 
                string.IsNullOrWhiteSpace(keyFileId) || string.IsNullOrWhiteSpace(command))
            {
                return "Error: 'host', 'username', 'keyFileId', and 'command' are all required parameters.";
            }

            _logger.LogInformation("Native SSH: Retrieving private key file ID '{KeyFileId}' for user '{UserId}'", keyFileId, userId);

            string privateKeyContent;
            try
            {
                var (content, _) = await _driveService.ReadFileContentAsync(userId.Value.ToString(), keyFileId);
                privateKeyContent = content;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Native SSH: Failed to fetch key file ID '{KeyFileId}' from Google Drive for user '{UserId}'", keyFileId, userId);
                return $"Error: Failed to fetch private key from Google Drive — {ex.Message}";
            }

            if (string.IsNullOrWhiteSpace(privateKeyContent))
            {
                return "Error: Fetching private key returned empty content.";
            }

            _logger.LogInformation("Native SSH: Establishing connection to '{Username}@{Host}:{Port}'", username, host, port);

            try
            {
                using var keyStream = new MemoryStream(Encoding.UTF8.GetBytes(privateKeyContent));
                var keyFile = new PrivateKeyFile(keyStream);
                var connectionInfo = new ConnectionInfo(host, port, username, new PrivateKeyAuthenticationMethod(username, keyFile))
                {
                    Timeout = TimeSpan.FromMilliseconds(timeout)
                };

                using var client = new SshClient(connectionInfo);
                
                // Set up async connect wrapper with timeout
                var connectTask = Task.Run(() => client.Connect());
                var timeoutTask = Task.Delay(timeout);

                var completedTask = await Task.WhenAny(connectTask, timeoutTask);
                if (completedTask == timeoutTask)
                {
                    try { client.Disconnect(); } catch { }
                    return $"Error: Connection timeout to {username}@{host}:{port} after {timeout}ms";
                }

                // Await connectTask to propagate any exceptions
                await connectTask;

                _logger.LogInformation("Native SSH: Connected successfully. Executing command: {Command}", command);

                using var cmd = client.CreateCommand(command);
                cmd.CommandTimeout = TimeSpan.FromMilliseconds(timeout);

                // Run execution in separate thread to prevent blocking
                var executeTask = Task.Run(() => cmd.Execute());
                var execCompletedTask = await Task.WhenAny(executeTask, Task.Delay(timeout));

                if (execCompletedTask != executeTask)
                {
                    try { client.Disconnect(); } catch { }
                    return $"Error: Command execution timed out after {timeout}ms";
                }

                await executeTask;

                var stdout = cmd.Result;
                var stderr = cmd.Error;
                var exitCode = cmd.ExitStatus;

                client.Disconnect();

                var sb = new StringBuilder();
                if (!string.IsNullOrEmpty(stdout))
                {
                    sb.AppendLine("STDOUT:");
                    sb.AppendLine(stdout);
                }
                if (!string.IsNullOrEmpty(stderr))
                {
                    sb.AppendLine("STDERR:");
                    sb.AppendLine(stderr);
                }
                sb.AppendLine($"Exit code: {exitCode}");

                return sb.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Native SSH: Execution error during connection or command run on host '{Host}'", host);
                return $"Error: SSH operation failed — {ex.Message}";
            }
        }
    }
}
