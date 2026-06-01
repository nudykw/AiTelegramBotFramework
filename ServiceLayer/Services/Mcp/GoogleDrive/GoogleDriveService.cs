using Google.Apis.Drive.v3;
using DriveFile = Google.Apis.Drive.v3.Data.File;
using Google.Apis.Upload;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace ServiceLayer.Services.Mcp.GoogleDrive;

public class GoogleDriveService : IGoogleDriveService
{
    private readonly IGoogleDriveAuthService _authService;
    private readonly AppSettings _settings;
    private readonly ILogger<GoogleDriveService> _logger;

    public GoogleDriveService(
        IGoogleDriveAuthService authService,
        AppSettings settings,
        ILogger<GoogleDriveService> logger)
    {
        _authService = authService;
        _settings = settings;
        _logger = logger;
    }

    private string ResolveUserId(string? userId)
    {
        if (!string.IsNullOrEmpty(userId))
        {
            return userId;
        }

        // Check context first
        var contextUserId = McpContext.UserId;
        if (contextUserId != null)
        {
            return contextUserId.Value.ToString();
        }

        // Check Environment variables
        var envUserId = Environment.GetEnvironmentVariable("USER_ID") 
            ?? Environment.GetEnvironmentVariable("WORKSPACE_USER_ID");
        if (!string.IsNullOrEmpty(envUserId))
        {
            return envUserId;
        }

        // Fall back to default
        return _settings.GoogleDriveSettings?.DefaultUserId ?? "default";
    }

    private string ResolveRootFolderName(string userId)
    {
        var template = _settings.GoogleDriveSettings?.RootFolderTemplate ?? "AppWorkspace_{UserId}";
        return template.Replace("{UserId}", userId);
    }

    private async Task<string> GetOrCreateRootFolderIdAsync(DriveService service, string userId)
    {
        var rootFolderName = ResolveRootFolderName(userId);
        _logger.LogInformation("Resolving sandbox root folder '{RootFolder}' for user ID '{UserId}'", rootFolderName, userId);

        // Check if root folder already exists in the accessible space of Google Drive (including shared folders)
        var listRequest = service.Files.List();
        listRequest.Q = $"mimeType = 'application/vnd.google-apps.folder' and name = '{rootFolderName}' and trashed = false";
        listRequest.Fields = "files(id, name)";
        listRequest.PageSize = 1;

        var listResult = await listRequest.ExecuteAsync();
        if (listResult.Files != null && listResult.Files.Count > 0)
        {
            var folderId = listResult.Files[0].Id;
            _logger.LogInformation("Found existing sandbox root folder '{RootFolder}' with ID '{Id}'", rootFolderName, folderId);
            return folderId;
        }

        // Create the root folder if it doesn't exist
        _logger.LogInformation("Creating new sandbox root folder '{RootFolder}' in Google Drive root", rootFolderName);
        var folderMetadata = new DriveFile
        {
            Name = rootFolderName,
            MimeType = "application/vnd.google-apps.folder",
            Parents = new List<string> { "root" }
        };

        var createRequest = service.Files.Create(folderMetadata);
        createRequest.Fields = "id";
        var folder = await createRequest.ExecuteAsync();
        _logger.LogInformation("Created new sandbox root folder '{RootFolder}' with ID '{Id}'", rootFolderName, folder.Id);
        return folder.Id;
    }

    private async Task<Dictionary<string, GoogleDriveItem>> BuildWorkspaceMapAsync(DriveService service, string rootFolderId)
    {
        var itemsMap = new Dictionary<string, GoogleDriveItem>();
        var queue = new Queue<(string FolderId, string ParentPath)>();
        queue.Enqueue((rootFolderId, ""));

        while (queue.Count > 0)
        {
            var (currentFolderId, parentPath) = queue.Dequeue();

            var listRequest = service.Files.List();
            listRequest.Q = $"'{currentFolderId}' in parents";
            listRequest.Fields = "nextPageToken, files(id, name, mimeType, description, size, md5Checksum, trashed)";
            listRequest.PageSize = 1000;

            do
            {
                var result = await listRequest.ExecuteAsync();
                if (result.Files == null) break;

                foreach (var file in result.Files)
                {
                    bool isFolder = file.MimeType == "application/vnd.google-apps.folder";
                    bool isMarkdown = file.MimeType == "text/markdown" || 
                                     (!string.IsNullOrEmpty(file.Name) && file.Name.EndsWith(".md", StringComparison.OrdinalIgnoreCase));
                    bool isSshFile = !string.IsNullOrEmpty(file.Name) && 
                                    (file.Name.StartsWith("id_", StringComparison.OrdinalIgnoreCase) || 
                                     file.Name.EndsWith(".pem", StringComparison.OrdinalIgnoreCase) || 
                                     file.Name.EndsWith(".key", StringComparison.OrdinalIgnoreCase) || 
                                     file.Name.Equals("config", StringComparison.OrdinalIgnoreCase));

                    if (!isFolder && !isMarkdown && !isSshFile)
                    {
                        // Filter: keep only folders, markdown, and standard SSH files
                        continue;
                    }

                    var virtualPath = parentPath + "/" + file.Name;
                    var item = new GoogleDriveItem
                    {
                        Id = file.Id,
                        Name = file.Name,
                        Type = isFolder ? "folder" : "file",
                        VirtualPath = virtualPath,
                        Description = file.Description,
                        Size = file.Size,
                        Md5Checksum = file.Md5Checksum,
                        IsTrashed = file.Trashed ?? false
                    };

                    itemsMap[file.Id] = item;

                    if (isFolder)
                    {
                        queue.Enqueue((file.Id, virtualPath));
                    }
                }
                listRequest.PageToken = result.NextPageToken;
            } while (!string.IsNullOrEmpty(listRequest.PageToken));
        }

        return itemsMap;
    }

    private void ValidateFilename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name cannot be empty.");
        }
        if (name.Contains('/') || name.Contains('\\') || name.Contains(".."))
        {
            throw new ArgumentException($"Invalid name '{name}'. Traversal characters (/, \\, ..) are strictly prohibited.");
        }
    }

    public async Task<List<GoogleDriveItem>> ListAllAsync(
        string? userId, 
        string? folderId = null, 
        bool isRecursive = true, 
        bool includeDescription = true,
        bool includeTrashed = false)
    {
        var resolvedUser = ResolveUserId(userId);
        var service = await _authService.GetDriveServiceAsync();
        var rootId = await GetOrCreateRootFolderIdAsync(service, resolvedUser);
        
        var map = await BuildWorkspaceMapAsync(service, rootId);

        string targetPath = "";
        if (!string.IsNullOrEmpty(folderId))
        {
            // Security check: must exist in sandbox and be a folder
            if (!map.TryGetValue(folderId, out var targetFolder) || targetFolder.Type != "folder" || (!includeTrashed && targetFolder.IsTrashed))
            {
                throw new UnauthorizedAccessException($"Access Denied: Folder ID '{folderId}' is not found in the sandboxed workspace.");
            }
            targetPath = targetFolder.VirtualPath;
        }

        var results = new List<GoogleDriveItem>();
        foreach (var item in map.Values)
        {
            if (!includeTrashed && item.IsTrashed)
            {
                continue;
            }

            bool matches = false;
            if (string.IsNullOrEmpty(folderId))
            {
                if (isRecursive)
                {
                    matches = true;
                }
                else
                {
                    // Direct child of root: no other slashes after the leading one
                    matches = item.VirtualPath.IndexOf('/', 1) == -1;
                }
            }
            else
            {
                if (item.VirtualPath.StartsWith(targetPath + "/"))
                {
                    if (isRecursive)
                    {
                        matches = true;
                    }
                    else
                    {
                        // Direct child of target folder: no other slashes after targetPath + "/"
                        matches = item.VirtualPath.IndexOf('/', targetPath.Length + 1) == -1;
                    }
                }
            }

            if (matches)
            {
                results.Add(new GoogleDriveItem
                {
                    Id = item.Id,
                    Name = item.Name,
                    Type = item.Type,
                    VirtualPath = item.VirtualPath,
                    Description = includeDescription ? item.Description : null,
                    Size = item.Size,
                    Md5Checksum = item.Md5Checksum,
                    IsTrashed = item.IsTrashed
                });
            }
        }

        return results;
    }

    public async Task<(string Content, GoogleDriveItem Item)> ReadFileContentAsync(string? userId, string fileId)
    {
        var resolvedUser = ResolveUserId(userId);
        var service = await _authService.GetDriveServiceAsync();
        var rootId = await GetOrCreateRootFolderIdAsync(service, resolvedUser);

        // Security check: must exist inside sandbox
        var map = await BuildWorkspaceMapAsync(service, rootId);
        if (!map.TryGetValue(fileId, out var item) || item.Type != "file")
        {
            throw new UnauthorizedAccessException($"Access Denied: File ID '{fileId}' is not found in the sandboxed workspace.");
        }

        _logger.LogInformation("Reading file content for File ID '{FileId}' ('{Path}')", fileId, item.VirtualPath);

        var request = service.Files.Get(fileId);
        using var stream = new MemoryStream();
        await request.DownloadAsync(stream);
        stream.Position = 0;
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();

        return (content, item);
    }

    public async Task<GoogleDriveItem> WriteFileContentAsync(string? userId, string fileId, string content)
    {
        var resolvedUser = ResolveUserId(userId);
        var service = await _authService.GetDriveServiceAsync();
        var rootId = await GetOrCreateRootFolderIdAsync(service, resolvedUser);

        // Security check: must exist inside sandbox
        var map = await BuildWorkspaceMapAsync(service, rootId);
        if (!map.TryGetValue(fileId, out var item) || item.Type != "file")
        {
            throw new UnauthorizedAccessException($"Access Denied: File ID '{fileId}' is not found in the sandboxed workspace.");
        }

        _logger.LogInformation("Updating file content for File ID '{FileId}' ('{Path}')", fileId, item.VirtualPath);

        var fileMetadata = new DriveFile(); // keep metadata untouched
        byte[] byteArray = System.Text.Encoding.UTF8.GetBytes(content);
        using var stream = new MemoryStream(byteArray);

        var request = service.Files.Update(fileMetadata, fileId, stream, "text/markdown");
        request.Fields = "id, name, mimeType, description, size, md5Checksum";
        
        await UploadFileMediaAsync(request);
        var updatedFile = request.ResponseBody;

        return new GoogleDriveItem
        {
            Id = item.Id,
            Name = item.Name,
            Type = item.Type,
            VirtualPath = item.VirtualPath,
            Description = item.Description,
            Size = updatedFile?.Size ?? item.Size,
            Md5Checksum = updatedFile?.Md5Checksum ?? item.Md5Checksum,
            IsTrashed = item.IsTrashed
        };
    }

    public async Task<GoogleDriveItem> CreateItemAsync(string? userId, string name, string type, string description, string? parentId = null)
    {
        ValidateFilename(name);
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("Description is a mandatory parameter for creating files/folders.");
        }
        if (type != "file" && type != "folder")
        {
            throw new ArgumentException("Type must be either 'file' or 'folder'.");
        }

        var resolvedUser = ResolveUserId(userId);
        var service = await _authService.GetDriveServiceAsync();
        var rootId = await GetOrCreateRootFolderIdAsync(service, resolvedUser);

        string targetParentId = rootId;
        string parentVirtualPath = "";

        var map = await BuildWorkspaceMapAsync(service, rootId);

        if (!string.IsNullOrEmpty(parentId))
        {
            // Security check: parent must exist inside sandbox and be a folder
            if (!map.TryGetValue(parentId, out var parentItem) || parentItem.Type != "folder")
            {
                throw new UnauthorizedAccessException($"Access Denied: Parent folder ID '{parentId}' is not found in the sandboxed workspace.");
            }
            targetParentId = parentId;
            parentVirtualPath = parentItem.VirtualPath;
        }

        // Enforce name uniqueness inside the target folder
        var escapedName = name.Replace("'", "\\'");
        var duplicateQuery = $"name = '{escapedName}' and '{targetParentId}' in parents and trashed = false";
        var duplicateRequest = service.Files.List();
        duplicateRequest.Q = duplicateQuery;
        duplicateRequest.Fields = "files(id, name)";
        duplicateRequest.PageSize = 1;
        var duplicateResult = await duplicateRequest.ExecuteAsync();

        if (duplicateResult.Files != null && duplicateResult.Files.Count > 0)
        {
            if (type == "file")
            {
                throw new InvalidOperationException($"Error: A file named '{name}' already exists in this directory. Use the 'gdrive_write' tool to update its content instead of creating a new one.");
            }
            else
            {
                throw new InvalidOperationException($"Error: A folder named '{name}' already exists in this directory.");
            }
        }

        _logger.LogInformation("Creating {Type} '{Name}' in parent folder ID '{ParentId}'", type, name, targetParentId);

        var fileMetadata = new DriveFile
        {
            Name = name,
            Description = description,
            Parents = new List<string> { targetParentId }
        };

        DriveFile createdFile;

        if (type == "folder")
        {
            fileMetadata.MimeType = "application/vnd.google-apps.folder";
            var request = service.Files.Create(fileMetadata);
            request.Fields = "id, name, mimeType, description, size, md5Checksum";
            createdFile = await request.ExecuteAsync();
        }
        else
        {
            fileMetadata.MimeType = "text/markdown";
            byte[] byteArray = System.Text.Encoding.UTF8.GetBytes("");
            using var stream = new MemoryStream(byteArray);

            var request = service.Files.Create(fileMetadata, stream, "text/markdown");
            request.Fields = "id, name, mimeType, description, size, md5Checksum";

            await UploadFileMediaAsync(request);
            createdFile = request.ResponseBody;
        }

        return new GoogleDriveItem
        {
            Id = createdFile.Id,
            Name = createdFile.Name,
            Type = type,
            VirtualPath = parentVirtualPath + "/" + createdFile.Name,
            Description = createdFile.Description,
            Size = createdFile.Size,
            Md5Checksum = createdFile.Md5Checksum,
            IsTrashed = false
        };
    }

    public async Task<GoogleDriveItem> UpdateDescriptionAsync(string? userId, string id, string newDescription)
    {
        if (string.IsNullOrWhiteSpace(newDescription))
        {
            throw new ArgumentException("Description cannot be empty.");
        }

        var resolvedUser = ResolveUserId(userId);
        var service = await _authService.GetDriveServiceAsync();
        var rootId = await GetOrCreateRootFolderIdAsync(service, resolvedUser);

        // Security check: must exist inside sandbox
        var map = await BuildWorkspaceMapAsync(service, rootId);
        if (!map.TryGetValue(id, out var item))
        {
            throw new UnauthorizedAccessException($"Access Denied: Item ID '{id}' is not found in the sandboxed workspace.");
        }

        _logger.LogInformation("Updating description for Item ID '{Id}' ('{Path}')", id, item.VirtualPath);

        var fileMetadata = new DriveFile
        {
            Description = newDescription
        };

        var request = service.Files.Update(fileMetadata, id);
        request.Fields = "id, name, mimeType, description";
        var updatedFile = await request.ExecuteAsync();

        item.Description = updatedFile.Description;
        return item;
    }

    public async Task<bool> DeleteItemAsync(string? userId, string id)
    {
        var resolvedUser = ResolveUserId(userId);
        var service = await _authService.GetDriveServiceAsync();
        var rootId = await GetOrCreateRootFolderIdAsync(service, resolvedUser);

        if (id == rootId)
        {
            throw new InvalidOperationException("Access Denied: Deleting the sandboxed workspace root folder itself is strictly prohibited.");
        }

        // Security check: must exist inside sandbox
        var map = await BuildWorkspaceMapAsync(service, rootId);
        if (!map.TryGetValue(id, out var item))
        {
            throw new UnauthorizedAccessException($"Access Denied: Item ID '{id}' is not found in the sandboxed workspace.");
        }

        _logger.LogInformation("Moving Item ID '{Id}' ('{Path}') to Trash", id, item.VirtualPath);

        var fileMetadata = new DriveFile
        {
            Trashed = true
        };

        var request = service.Files.Update(fileMetadata, id);
        request.Fields = "id, name, mimeType, description, trashed";
        var updatedFile = await request.ExecuteAsync();

        return updatedFile.Trashed ?? false;
    }

    public async Task<GoogleDriveItem> RestoreItemAsync(string? userId, string id)
    {
        var resolvedUser = ResolveUserId(userId);
        var service = await _authService.GetDriveServiceAsync();
        var rootId = await GetOrCreateRootFolderIdAsync(service, resolvedUser);

        if (id == rootId)
        {
            throw new InvalidOperationException("Access Denied: Restoring the sandboxed workspace root folder is not applicable.");
        }

        // Security check: must exist inside sandbox
        var map = await BuildWorkspaceMapAsync(service, rootId);
        if (!map.TryGetValue(id, out var item))
        {
            throw new UnauthorizedAccessException($"Access Denied: Item ID '{id}' is not found in the sandboxed workspace.");
        }

        _logger.LogInformation("Restoring Item ID '{Id}' ('{Path}') from Trash", id, item.VirtualPath);

        var fileMetadata = new DriveFile
        {
            Trashed = false
        };

        var request = service.Files.Update(fileMetadata, id);
        request.Fields = "id, name, mimeType, description, size, md5Checksum, trashed";
        var updatedFile = await request.ExecuteAsync();

        return new GoogleDriveItem
        {
            Id = updatedFile.Id,
            Name = updatedFile.Name,
            Type = item.Type,
            VirtualPath = item.VirtualPath,
            Description = updatedFile.Description,
            Size = updatedFile.Size,
            Md5Checksum = updatedFile.Md5Checksum,
            IsTrashed = updatedFile.Trashed ?? false
        };
    }

    protected virtual async Task UploadFileMediaAsync(ResumableUpload upload)
    {
        var progress = await upload.UploadAsync();
        if (progress.Status == UploadStatus.Failed)
        {
            throw new InvalidOperationException("Failed to upload Google Drive file: " + progress.Exception?.Message, progress.Exception);
        }
    }

    // ── Version control ──────────────────────────────────────────────────────

    public async Task<List<GoogleDriveRevision>> ListRevisionsAsync(string? userId, string fileId)
    {
        var resolvedUser = ResolveUserId(userId);
        var service = await _authService.GetDriveServiceAsync();
        var rootId = await GetOrCreateRootFolderIdAsync(service, resolvedUser);

        // Sandbox check: file must exist inside the workspace
        var map = await BuildWorkspaceMapAsync(service, rootId);
        if (!map.TryGetValue(fileId, out var item) || item.Type != "file")
        {
            throw new UnauthorizedAccessException($"Access Denied: File ID '{fileId}' is not found in the sandboxed workspace.");
        }

        _logger.LogInformation("Listing revisions for File ID '{FileId}' ('{Path}')", fileId, item.VirtualPath);

        var listRequest = service.Revisions.List(fileId);
        listRequest.Fields = "revisions(id, modifiedTime, lastModifyingUser, size, keepForever, originalFilename)";

        var result = await listRequest.ExecuteAsync();
        var revisions = result.Revisions ?? [];

        return revisions.Select(r => new GoogleDriveRevision
        {
            Id = r.Id ?? string.Empty,
            ModifiedTime = r.ModifiedTimeDateTimeOffset?.UtcDateTime,
            LastModifyingUserEmail = r.LastModifyingUser?.EmailAddress,
            Size = r.Size,
            KeepForever = r.KeepForever ?? false,
            OriginalFilename = r.OriginalFilename
        }).ToList();
    }

    public async Task<(string Content, GoogleDriveRevision Revision)> ReadRevisionContentAsync(
        string? userId, string fileId, string revisionId)
    {
        var resolvedUser = ResolveUserId(userId);
        var service = await _authService.GetDriveServiceAsync();
        var rootId = await GetOrCreateRootFolderIdAsync(service, resolvedUser);

        // Sandbox check: file must exist inside the workspace
        var map = await BuildWorkspaceMapAsync(service, rootId);
        if (!map.TryGetValue(fileId, out var item) || item.Type != "file")
        {
            throw new UnauthorizedAccessException($"Access Denied: File ID '{fileId}' is not found in the sandboxed workspace.");
        }

        _logger.LogInformation("Reading revision '{RevisionId}' for File ID '{FileId}' ('{Path}')", revisionId, fileId, item.VirtualPath);

        // Fetch revision metadata
        var metaRequest = service.Revisions.Get(fileId, revisionId);
        metaRequest.Fields = "id, modifiedTime, lastModifyingUser, size, keepForever, originalFilename";
        var revisionMeta = await metaRequest.ExecuteAsync();

        // Download revision content — same pattern as Files.Get.DownloadAsync
        var downloadRequest = service.Revisions.Get(fileId, revisionId);
        using var stream = new MemoryStream();
        await downloadRequest.DownloadAsync(stream);
        stream.Position = 0;
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();

        var revision = new GoogleDriveRevision
        {
            Id = revisionMeta.Id ?? string.Empty,
            ModifiedTime = revisionMeta.ModifiedTimeDateTimeOffset?.UtcDateTime,
            LastModifyingUserEmail = revisionMeta.LastModifyingUser?.EmailAddress,
            Size = revisionMeta.Size,
            KeepForever = revisionMeta.KeepForever ?? false,
            OriginalFilename = revisionMeta.OriginalFilename
        };

        return (content, revision);
    }

    public async Task<(GoogleDriveItem Item, string NewRevisionId)> RestoreRevisionAsync(
        string? userId, string fileId, string revisionId, bool keepForever = false)
    {
        var resolvedUser = ResolveUserId(userId);
        var service = await _authService.GetDriveServiceAsync();
        var rootId = await GetOrCreateRootFolderIdAsync(service, resolvedUser);

        // Sandbox check: file must exist inside the workspace
        var map = await BuildWorkspaceMapAsync(service, rootId);
        if (!map.TryGetValue(fileId, out var item) || item.Type != "file")
        {
            throw new UnauthorizedAccessException($"Access Denied: File ID '{fileId}' is not found in the sandboxed workspace.");
        }

        _logger.LogInformation(
            "Restoring File ID '{FileId}' ('{Path}') to revision '{RevisionId}' (keepForever={KeepForever})",
            fileId, item.VirtualPath, revisionId, keepForever);

        // Read the old revision content (non-destructive read)
        var (oldContent, _) = await ReadRevisionContentAsync(userId, fileId, revisionId);

        // Write as a new revision — existing revisions are untouched
        var updatedItem = await WriteFileContentAsync(userId, fileId, oldContent);

        // Determine the ID of the newly-created revision
        var revisionsAfter = await ListRevisionsAsync(userId, fileId);
        var newRevision = revisionsAfter.LastOrDefault();
        var newRevisionId = newRevision?.Id ?? string.Empty;

        // Optionally pin the new revision so Google Drive doesn't auto-purge it
        if (keepForever && !string.IsNullOrEmpty(newRevisionId))
        {
            _logger.LogInformation("Pinning new revision '{NewRevisionId}' for File ID '{FileId}' with keepForever=true", newRevisionId, fileId);
            var driveRevision = new Google.Apis.Drive.v3.Data.Revision { KeepForever = true };
            var updateRequest = service.Revisions.Update(driveRevision, fileId, newRevisionId);
            updateRequest.Fields = "id, keepForever";
            await updateRequest.ExecuteAsync();
        }

        return (updatedItem, newRevisionId);
    }
}

