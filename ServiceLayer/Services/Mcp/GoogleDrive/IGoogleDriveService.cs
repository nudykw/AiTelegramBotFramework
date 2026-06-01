using System.Collections.Generic;
using System.Threading.Tasks;

namespace ServiceLayer.Services.Mcp.GoogleDrive;

public interface IGoogleDriveService
{
    Task<List<GoogleDriveItem>> ListAllAsync(
        string? userId, 
        string? folderId = null, 
        bool isRecursive = true, 
        bool includeDescription = true,
        bool includeTrashed = false);
    Task<(string Content, GoogleDriveItem Item)> ReadFileContentAsync(string? userId, string fileId);
    Task<GoogleDriveItem> WriteFileContentAsync(string? userId, string fileId, string content);
    Task<GoogleDriveItem> CreateItemAsync(string? userId, string name, string type, string description, string? parentId = null);
    Task<GoogleDriveItem> UpdateDescriptionAsync(string? userId, string id, string newDescription);
    Task<bool> DeleteItemAsync(string? userId, string id);
    Task<GoogleDriveItem> RestoreItemAsync(string? userId, string id);

    // ── Version control ──────────────────────────────────────────────────────
    Task<List<GoogleDriveRevision>> ListRevisionsAsync(string? userId, string fileId);
    Task<(string Content, GoogleDriveRevision Revision)> ReadRevisionContentAsync(string? userId, string fileId, string revisionId);
    Task<(GoogleDriveItem Item, string NewRevisionId)> RestoreRevisionAsync(string? userId, string fileId, string revisionId, bool keepForever = false);
}
