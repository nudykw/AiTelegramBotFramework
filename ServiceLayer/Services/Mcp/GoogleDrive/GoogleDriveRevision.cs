namespace ServiceLayer.Services.Mcp.GoogleDrive;

public class GoogleDriveRevision
{
    public string Id { get; set; } = string.Empty;
    public DateTime? ModifiedTime { get; set; }
    public string? LastModifyingUserEmail { get; set; }
    public long? Size { get; set; }
    public bool KeepForever { get; set; }
    public string? OriginalFilename { get; set; }
}
