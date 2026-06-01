using Google.Apis.Drive.v3;
using System.Threading.Tasks;

namespace ServiceLayer.Services.Mcp.GoogleDrive;

public interface IGoogleDriveAuthService
{
    Task<DriveService> GetDriveServiceAsync();
    void ResetCache();
}
