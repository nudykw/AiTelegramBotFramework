using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Http;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceLayer.Services.Mcp.GoogleDrive;

public class GoogleDriveAuthService : IGoogleDriveAuthService
{
    private readonly AppSettings _settings;
    private readonly ILogger<GoogleDriveAuthService> _logger;
    private readonly object _lock = new();
    private DriveService? _cachedService;

    public GoogleDriveAuthService(AppSettings settings, ILogger<GoogleDriveAuthService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public void ResetCache()
    {
        lock (_lock)
        {
            _cachedService = null;
        }
        _logger.LogInformation("Google Drive authentication cache has been successfully reset.");
    }

    public async Task<DriveService> GetDriveServiceAsync()
    {
        if (_cachedService != null)
        {
            return _cachedService;
        }

        var config = _settings.GoogleDriveSettings ?? new GoogleDriveSettings();
        var authType = config.AuthType;

        if (!string.IsNullOrEmpty(authType) && 
            !string.Equals(authType, "OAuth", StringComparison.OrdinalIgnoreCase) && 
            !string.Equals(authType, "ServiceAccount", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported AuthType '{authType}' configured for Google Drive. Must be either 'OAuth' or 'ServiceAccount'.");
        }

        bool useServiceAccount = string.Equals(authType, "ServiceAccount", StringComparison.OrdinalIgnoreCase);

        if (useServiceAccount)
        {
            var serviceAccountPath = Path.GetFullPath(config.ServiceAccountKeyPath);

            // Fallback for Service Account key filename (support both .credentials.json and credentials.json)
            if (!File.Exists(serviceAccountPath))
            {
                var altPath = Path.Combine(Path.GetDirectoryName(serviceAccountPath) ?? "", "credentials.json");
                if (File.Exists(altPath))
                {
                    serviceAccountPath = altPath;
                }
            }

            if (!File.Exists(serviceAccountPath))
            {
                throw new FileNotFoundException($"Google Drive Service Account key file not found at '{serviceAccountPath}'.");
            }

            _logger.LogInformation("Attempting Google Drive authentication via ServiceAccount using file {Path}", Path.GetFileName(serviceAccountPath));
            
            try
            {
                IConfigurableHttpClientInitializer credential = await LoadServiceAccountCredentialAsync(config, serviceAccountPath);
                var testService = CreateDriveService(credential);
                await VerifyServiceAsync(testService);

                _logger.LogInformation("Google Drive authentication successful via ServiceAccount");
                lock (_lock)
                {
                    _cachedService = testService;
                }
                return _cachedService;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Google Drive ServiceAccount authentication failed.");
                throw;
            }
        }
        else
        {
            var oauthPath = Path.GetFullPath(config.ClientSecretsPath);

            if (!File.Exists(oauthPath))
            {
                throw new FileNotFoundException($"Google Drive OAuth client secrets file not found at '{oauthPath}'.");
            }

            _logger.LogInformation("Attempting Google Drive authentication via OAuth using file {Path}", Path.GetFileName(oauthPath));

            try
            {
                IConfigurableHttpClientInitializer credential = await LoadOAuthCredentialAsync(config, oauthPath);
                var testService = CreateDriveService(credential);
                await VerifyServiceAsync(testService);

                _logger.LogInformation("Google Drive authentication successful via OAuth");
                lock (_lock)
                {
                    _cachedService = testService;
                }
                return _cachedService;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Google Drive OAuth authentication failed.");
                throw;
            }
        }
    }

    protected virtual async Task VerifyServiceAsync(DriveService service)
    {
        var aboutRequest = service.About.Get();
        aboutRequest.Fields = "user,storageQuota";
        var about = await aboutRequest.ExecuteAsync();
        
        if (about.User?.EmailAddress != null && 
            about.User.EmailAddress.EndsWith(".gserviceaccount.com", StringComparison.OrdinalIgnoreCase))
        {
            if (about.StorageQuota?.Limit == 0)
            {
                throw new InvalidOperationException("Service Account has 0 storage quota and cannot write files.");
            }
        }
    }

    protected virtual async Task<IConfigurableHttpClientInitializer> LoadOAuthCredentialAsync(GoogleDriveSettings config, string secretsPath)
    {
        var secretsDir = Path.GetDirectoryName(secretsPath);
        if (string.IsNullOrEmpty(secretsDir)) secretsDir = ".secrets";

        var clientSecretFile = Directory.GetFiles(secretsDir, "client_secret_*.json").FirstOrDefault();
        if (clientSecretFile == null)
        {
            throw new FileNotFoundException("OAuth client secrets file (client_secret_*.json) not found in secrets directory.");
        }

        ClientSecrets secrets;
        using (var stream = new FileStream(clientSecretFile, FileMode.Open, FileAccess.Read))
        {
            secrets = GoogleClientSecrets.FromStream(stream).Secrets;
        }

        var store = new SingleFileTokenStore(secretsPath);
        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = secrets,
            Scopes = new[] { DriveService.Scope.Drive },
            DataStore = store
        });

        var token = await flow.LoadTokenAsync("user", CancellationToken.None);
        if (token == null)
        {
            throw new InvalidOperationException("OAuth token not found in token store.");
        }

        var credential = new UserCredential(flow, "user", token);
        return credential;
    }

    protected virtual Task<IConfigurableHttpClientInitializer> LoadServiceAccountCredentialAsync(GoogleDriveSettings config, string keyPath)
    {
        using var stream = new FileStream(keyPath, FileMode.Open, FileAccess.Read);
        var credential = GoogleCredential.FromStream(stream)
            .CreateScoped(DriveService.Scope.Drive);
        return Task.FromResult<IConfigurableHttpClientInitializer>(credential);
    }

    protected virtual DriveService CreateDriveService(IConfigurableHttpClientInitializer credential)
    {
        return new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "GptChatTelegramBotGoogleDriveMCP"
        });
    }
}

public class SingleFileTokenStore : IDataStore
{
    private readonly string _filePath;

    public SingleFileTokenStore(string filePath)
    {
        _filePath = filePath;
    }

    public Task StoreAsync<T>(string key, T value)
    {
        var json = JsonConvert.SerializeObject(value, Formatting.Indented);
        File.WriteAllText(_filePath, json);
        return Task.CompletedTask;
    }

    public Task DeleteAsync<T>(string key)
    {
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }
        return Task.CompletedTask;
    }

    public Task<T?> GetAsync<T>(string key)
    {
        if (!File.Exists(_filePath))
        {
            return Task.FromResult<T?>(default);
        }
        try
        {
            var json = File.ReadAllText(_filePath);
            var value = JsonConvert.DeserializeObject<T>(json);
            return Task.FromResult(value);
        }
        catch
        {
            return Task.FromResult<T?>(default);
        }
    }

    public Task ClearAsync()
    {
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }
        return Task.CompletedTask;
    }
}
