namespace ServiceLayer.Services.Mcp.GoogleDrive;

public class GoogleDriveItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "file"; // "file" or "folder"
    public string VirtualPath { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long? Size { get; set; }
    public string? Md5Checksum { get; set; }
    public bool IsTrashed { get; set; }
}
