using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ServiceLayer.Services.Mcp.GoogleDrive;

namespace ServiceLayer.Services.Mcp;

public class GoogleDriveMcpTools : INativeMcpTool
{
    private readonly IGoogleDriveService _driveService;
    private readonly ILogger<GoogleDriveMcpTools> _logger;
    private readonly string _toolName;

    // Tool names
    public const string ToolList              = "gdrive_list";
    public const string ToolRead              = "gdrive_read";
    public const string ToolWrite             = "gdrive_write";
    public const string ToolCreate            = "gdrive_create";
    public const string ToolUpdateDescription = "gdrive_update_description";
    public const string ToolDelete            = "gdrive_delete";
    public const string ToolRestore           = "gdrive_restore";

    // Version control tool names
    public const string ToolListVersions   = "gdrive_list_versions";
    public const string ToolReadVersion    = "gdrive_read_version";
    public const string ToolRestoreVersion = "gdrive_restore_version";

    public string Name => _toolName;
    public string ServerName => "native-gdrive";


    public string Description => _toolName switch
    {
        ToolList => "Recursively obtains all markdown files and folders inside the isolated Google Drive workspace. " +
                    "Returns paths, IDs, types, and descriptions to understand file context without downloading.",
        ToolRead => "Downloads and reads the complete text content of a markdown (.md) file inside the sandboxed workspace using its fileId.",
        ToolWrite => "Updates the complete text content of an existing markdown file in the sandboxed workspace.",
        ToolCreate => "Creates a new markdown empty file or folder in the workspace. Recording a short description of the file's purpose in the metadata is mandatory.",
        ToolUpdateDescription => "Updates the metadata description field of a file or folder inside the workspace. Use this when the purpose of a file changes.",
        ToolDelete => "Moves a markdown file or folder to the trash. The root isolated workspace folder itself cannot be deleted.",
        ToolRestore => "Restores a markdown file or folder from the trash back to its active state.",
        ToolListVersions   => "Lists all saved revisions (versions) of a markdown file. Returns revision IDs, modification dates, authors and sizes. Use before gdrive_read_version or gdrive_restore_version.",
        ToolReadVersion    => "Reads the text content of a specific historical revision of a markdown file. This is a read-only operation and does not modify the file.",
        ToolRestoreVersion => "Restores a file to a specific historical revision by writing its content as a new revision. All previous revisions remain intact — no data is lost.",
        _ => string.Empty
    };

    public string JsonSchema => _toolName switch
    {
        ToolList => """
            {
              "type": "object",
              "properties": {
                "folderId": {
                  "type": "string",
                  "description": "Optional unique ID of a folder to list. If omitted, lists from the root workspace directory."
                },
                "isRecursive": {
                  "type": "boolean",
                  "description": "Optional. If true (default), lists all files recursively. If false, lists only direct children of the folder."
                },
                "includeDescription": {
                  "type": "boolean",
                  "description": "Optional. If true (default), includes file description metadata. If false, description is omitted."
                },
                "includeTrashed": {
                  "type": "boolean",
                  "description": "Optional. If true, lists all files (including those in the trash). If false (default), only lists active files."
                }
              }
            }
            """,
        ToolRead => """
            {
              "type": "object",
              "properties": {
                "fileId": {
                  "type": "string",
                  "description": "The unique ID of the file in Google Drive to read."
                }
              },
              "required": ["fileId"]
            }
            """,
        ToolWrite => """
            {
              "type": "object",
              "properties": {
                "fileId": {
                  "type": "string",
                  "description": "The unique ID of the file in Google Drive to update."
                },
                "content": {
                  "type": "string",
                  "description": "The complete new text content of the markdown file."
                }
              },
              "required": ["fileId", "content"]
            }
            """,
        ToolCreate => """
            {
              "type": "object",
              "properties": {
                "name": {
                  "type": "string",
                  "description": "The name of the file or folder (e.g., 'todo.md' or 'archives'). If creating a file, it must have the '.md' extension."
                },
                "type": {
                  "type": "string",
                  "enum": ["file", "folder"],
                  "description": "The type of the item: 'file' or 'folder'."
                },
                "description": {
                  "type": "string",
                  "description": "A mandatory brief description of what this file/folder contains or represents."
                },
                "parentId": {
                  "type": "string",
                  "description": "Optional parent folder ID. If null, creates the item in the workspace root."
                }
              },
              "required": ["name", "type", "description"]
            }
            """,
        ToolUpdateDescription => """
            {
              "type": "object",
              "properties": {
                "id": {
                  "type": "string",
                  "description": "The unique ID of the file or folder to update description for."
                },
                "newDescription": {
                  "type": "string",
                  "description": "The new detailed description context."
                }
              },
              "required": ["id", "newDescription"]
            }
            """,
        ToolDelete => """
            {
              "type": "object",
              "properties": {
                "id": {
                  "type": "string",
                  "description": "The unique ID of the file or folder to move to trash."
                }
              },
              "required": ["id"]
            }
            """,
        ToolRestore => """
            {
              "type": "object",
              "properties": {
                "id": {
                  "type": "string",
                  "description": "The unique ID of the file or folder to restore from trash."
                }
              },
              "required": ["id"]
            }
            """,
        ToolListVersions => """
            {
              "type": "object",
              "properties": {
                "fileId": {
                  "type": "string",
                  "description": "The unique ID of the markdown file whose revision history to list."
                }
              },
              "required": ["fileId"]
            }
            """,
        ToolReadVersion => """
            {
              "type": "object",
              "properties": {
                "fileId": {
                  "type": "string",
                  "description": "The unique ID of the markdown file."
                },
                "revisionId": {
                  "type": "string",
                  "description": "The revision ID to read (obtain from gdrive_list_versions)."
                }
              },
              "required": ["fileId", "revisionId"]
            }
            """,
        ToolRestoreVersion => """
            {
              "type": "object",
              "properties": {
                "fileId": {
                  "type": "string",
                  "description": "The unique ID of the markdown file to restore."
                },
                "revisionId": {
                  "type": "string",
                  "description": "The revision ID to restore from (obtain from gdrive_list_versions)."
                },
                "keepForever": {
                  "type": "boolean",
                  "description": "Optional. If true, the newly created revision will be pinned as 'keep forever' so Google Drive does not auto-purge it. Default: false."
                }
              },
              "required": ["fileId", "revisionId"]
            }
            """,
        _ => "{\"type\":\"object\",\"properties\":{}}"
    };
    public GoogleDriveMcpTools(
        string toolName,
        IGoogleDriveService driveService,
        ILogger<GoogleDriveMcpTools> logger)
    {
        _toolName = toolName;
        _driveService = driveService;
        _logger = logger;
    }

    public async Task<string> ExecuteAsync(string argumentsJson)
    {
        var userId = McpContext.UserId;
        _logger.LogInformation("Executing Google Drive MCP tool '{Tool}' for UserId={UserId}", _toolName, userId);

        try
        {
            return _toolName switch
            {
                ToolList              => await ListWorkspaceAsync(argumentsJson),
                ToolRead              => await ReadFileAsync(argumentsJson),
                ToolWrite             => await WriteFileAsync(argumentsJson),
                ToolCreate            => await CreateItemAsync(argumentsJson),
                ToolUpdateDescription => await UpdateDescriptionAsync(argumentsJson),
                ToolDelete            => await DeleteItemAsync(argumentsJson),
                ToolRestore           => await RestoreItemAsync(argumentsJson),
                ToolListVersions      => await ListVersionsAsync(argumentsJson),
                ToolReadVersion       => await ReadVersionAsync(argumentsJson),
                ToolRestoreVersion    => await RestoreVersionAsync(argumentsJson),
                _                     => SerializeError($"Unknown tool '{_toolName}'.")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing Google Drive MCP tool '{Tool}'", _toolName);
            return SerializeError(ex.Message);
        }
    }

    private static string SerializeResult<T>(T result)
    {
        return JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    private static string SerializeError(string message)
    {
        return JsonSerializer.Serialize(new { error = message }, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    private async Task<string> ListWorkspaceAsync(string argumentsJson)
    {
        string? folderId = null;
        bool isRecursive = true;
        bool includeDescription = true;
        bool includeTrashed = false;

        if (!string.IsNullOrEmpty(argumentsJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(argumentsJson);
                var root = doc.RootElement;
                
                if (root.TryGetProperty("folderId", out var folderIdProp) && folderIdProp.ValueKind == JsonValueKind.String)
                {
                    folderId = folderIdProp.GetString();
                }
                if (root.TryGetProperty("isRecursive", out var isRecursiveProp) && 
                    (isRecursiveProp.ValueKind == JsonValueKind.True || isRecursiveProp.ValueKind == JsonValueKind.False))
                {
                    isRecursive = isRecursiveProp.GetBoolean();
                }
                if (root.TryGetProperty("includeDescription", out var includeDescriptionProp) && 
                    (includeDescriptionProp.ValueKind == JsonValueKind.True || includeDescriptionProp.ValueKind == JsonValueKind.False))
                {
                    includeDescription = includeDescriptionProp.GetBoolean();
                }
                if (root.TryGetProperty("includeTrashed", out var includeTrashedProp) && 
                    (includeTrashedProp.ValueKind == JsonValueKind.True || includeTrashedProp.ValueKind == JsonValueKind.False))
                {
                    includeTrashed = includeTrashedProp.GetBoolean();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse gdrive_list arguments, using defaults.");
            }
        }

        var items = await _driveService.ListAllAsync(null, folderId, isRecursive, includeDescription, includeTrashed);
        return SerializeResult(items);
    }

    private async Task<string> ReadFileAsync(string argumentsJson)
    {
        using var doc = JsonDocument.Parse(argumentsJson);
        var root = doc.RootElement;
        
        if (!root.TryGetProperty("fileId", out var fileIdProp) || string.IsNullOrEmpty(fileIdProp.GetString()))
        {
            return SerializeError("Argument 'fileId' is required.");
        }

        var fileId = fileIdProp.GetString()!;
        var (content, item) = await _driveService.ReadFileContentAsync(null, fileId);
        return SerializeResult(new 
        { 
            fileId, 
            content,
            size = item.Size,
            md5Checksum = item.Md5Checksum,
            description = item.Description
        });
    }

    private async Task<string> WriteFileAsync(string argumentsJson)
    {
        using var doc = JsonDocument.Parse(argumentsJson);
        var root = doc.RootElement;

        if (!root.TryGetProperty("fileId", out var fileIdProp) || string.IsNullOrEmpty(fileIdProp.GetString()))
        {
            return SerializeError("Argument 'fileId' is required.");
        }
        if (!root.TryGetProperty("content", out var contentProp))
        {
            return SerializeError("Argument 'content' is required.");
        }

        var fileId = fileIdProp.GetString()!;
        var content = contentProp.GetString() ?? string.Empty;

        var item = await _driveService.WriteFileContentAsync(null, fileId, content);
        return SerializeResult(new { success = true, item });
    }

    private async Task<string> CreateItemAsync(string argumentsJson)
    {
        using var doc = JsonDocument.Parse(argumentsJson);
        var root = doc.RootElement;

        if (!root.TryGetProperty("name", out var nameProp) || string.IsNullOrEmpty(nameProp.GetString()))
        {
            return SerializeError("Argument 'name' is required.");
        }
        if (!root.TryGetProperty("type", out var typeProp) || string.IsNullOrEmpty(typeProp.GetString()))
        {
            return SerializeError("Argument 'type' is required.");
        }
        if (!root.TryGetProperty("description", out var descProp) || string.IsNullOrEmpty(descProp.GetString()))
        {
            return SerializeError("Argument 'description' is required.");
        }

        var name = nameProp.GetString()!;
        var type = typeProp.GetString()!;
        var description = descProp.GetString()!;
        string? parentId = root.TryGetProperty("parentId", out var parentIdProp) ? parentIdProp.GetString() : null;

        // Validation for files ending with .md
        if (type == "file" && !name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            return SerializeError("Markdown files must end with '.md' extension.");
        }

        var item = await _driveService.CreateItemAsync(null, name, type, description, parentId);
        return SerializeResult(item);
    }

    private async Task<string> UpdateDescriptionAsync(string argumentsJson)
    {
        using var doc = JsonDocument.Parse(argumentsJson);
        var root = doc.RootElement;

        if (!root.TryGetProperty("id", out var idProp) || string.IsNullOrEmpty(idProp.GetString()))
        {
            return SerializeError("Argument 'id' is required.");
        }
        if (!root.TryGetProperty("newDescription", out var newDescProp) || string.IsNullOrEmpty(newDescProp.GetString()))
        {
            return SerializeError("Argument 'newDescription' is required.");
        }

        var id = idProp.GetString()!;
        var newDescription = newDescProp.GetString()!;

        var item = await _driveService.UpdateDescriptionAsync(null, id, newDescription);
        return SerializeResult(item);
    }

    private async Task<string> DeleteItemAsync(string argumentsJson)
    {
        using var doc = JsonDocument.Parse(argumentsJson);
        var root = doc.RootElement;

        if (!root.TryGetProperty("id", out var idProp) || string.IsNullOrEmpty(idProp.GetString()))
        {
            return SerializeError("Argument 'id' is required.");
        }

        var id = idProp.GetString()!;
        var trashed = await _driveService.DeleteItemAsync(null, id);
        return SerializeResult(new { success = trashed, id });
    }

    private async Task<string> RestoreItemAsync(string argumentsJson)
    {
        using var doc = JsonDocument.Parse(argumentsJson);
        var root = doc.RootElement;

        if (!root.TryGetProperty("id", out var idProp) || string.IsNullOrEmpty(idProp.GetString()))
        {
            return SerializeError("Argument 'id' is required.");
        }

        var id = idProp.GetString()!;
        var item = await _driveService.RestoreItemAsync(null, id);
        return SerializeResult(new { success = true, item });
    }

    // ── Version control handlers ─────────────────────────────────────────────

    private async Task<string> ListVersionsAsync(string argumentsJson)
    {
        using var doc = JsonDocument.Parse(argumentsJson);
        var root = doc.RootElement;

        if (!root.TryGetProperty("fileId", out var fileIdProp) || string.IsNullOrEmpty(fileIdProp.GetString()))
        {
            return SerializeError("Argument 'fileId' is required.");
        }

        var fileId = fileIdProp.GetString()!;
        var revisions = await _driveService.ListRevisionsAsync(null, fileId);
        return SerializeResult(new { fileId, revisions });
    }

    private async Task<string> ReadVersionAsync(string argumentsJson)
    {
        using var doc = JsonDocument.Parse(argumentsJson);
        var root = doc.RootElement;

        if (!root.TryGetProperty("fileId", out var fileIdProp) || string.IsNullOrEmpty(fileIdProp.GetString()))
        {
            return SerializeError("Argument 'fileId' is required.");
        }
        if (!root.TryGetProperty("revisionId", out var revisionIdProp) || string.IsNullOrEmpty(revisionIdProp.GetString()))
        {
            return SerializeError("Argument 'revisionId' is required.");
        }

        var fileId = fileIdProp.GetString()!;
        var revisionId = revisionIdProp.GetString()!;

        var (content, revision) = await _driveService.ReadRevisionContentAsync(null, fileId, revisionId);
        return SerializeResult(new
        {
            fileId,
            revisionId,
            content,
            modifiedTime = revision.ModifiedTime,
            size = revision.Size,
            lastModifyingUserEmail = revision.LastModifyingUserEmail
        });
    }

    private async Task<string> RestoreVersionAsync(string argumentsJson)
    {
        using var doc = JsonDocument.Parse(argumentsJson);
        var root = doc.RootElement;

        if (!root.TryGetProperty("fileId", out var fileIdProp) || string.IsNullOrEmpty(fileIdProp.GetString()))
        {
            return SerializeError("Argument 'fileId' is required.");
        }
        if (!root.TryGetProperty("revisionId", out var revisionIdProp) || string.IsNullOrEmpty(revisionIdProp.GetString()))
        {
            return SerializeError("Argument 'revisionId' is required.");
        }

        var fileId = fileIdProp.GetString()!;
        var revisionId = revisionIdProp.GetString()!;
        var keepForever = root.TryGetProperty("keepForever", out var keepProp) &&
                          (keepProp.ValueKind == JsonValueKind.True || keepProp.ValueKind == JsonValueKind.False)
                          ? keepProp.GetBoolean()
                          : false;

        var (item, newRevisionId) = await _driveService.RestoreRevisionAsync(null, fileId, revisionId, keepForever);
        return SerializeResult(new
        {
            success = true,
            fileId,
            restoredFromRevisionId = revisionId,
            newRevisionId,
            item
        });
    }
}
