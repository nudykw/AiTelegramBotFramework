namespace ServiceLayer.Services.Mcp.GoogleDrive;

public class GoogleDriveSettings
{
    public string AuthType { get; set; } = "OAuth"; // "OAuth" or "ServiceAccount"
    public string ServiceAccountKeyPath { get; set; } = ".secrets/.credentials.json";
    public string ClientSecretsPath { get; set; } = ".secrets/.gdrive-server-credentials.json";
    public string TokenStorePath { get; set; } = ".secrets/token_store";
    public string RootFolderTemplate { get; set; } = "AppWorkspace_{UserId}";
    public string DefaultUserId { get; set; } = "default";
}
