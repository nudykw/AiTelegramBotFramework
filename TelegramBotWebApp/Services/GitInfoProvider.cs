using System.Text.Json;
using System.Text.Json.Serialization;

namespace TelegramBotWebApp.Services;

public class GitInfo
{
    [JsonPropertyName("CommitHash")]
    public string CommitHash { get; set; } = "unknown";

    [JsonPropertyName("CommitMessage")]
    public string CommitMessage { get; set; } = "unknown";

    [JsonPropertyName("CommitDate")]
    public string CommitDate { get; set; } = "unknown";
}

public static class GitInfoProvider
{
    private static readonly GitInfo _gitInfo;

    static GitInfoProvider()
    {
        try
        {
            string? configsBase = null;
            var currentDir = new DirectoryInfo(AppContext.BaseDirectory);
            while (currentDir != null)
            {
                var candidate = Path.Combine(currentDir.FullName, "Configs");
                if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "appsettings.json")))
                {
                    configsBase = candidate;
                    break;
                }
                currentDir = currentDir.Parent;
            }
            configsBase ??= Path.Combine(AppContext.BaseDirectory, "Configs");
            var filePath = Path.Combine(configsBase, "git_info.json");

            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                _gitInfo = JsonSerializer.Deserialize<GitInfo>(json) ?? new GitInfo();
            }
            else
            {
                _gitInfo = new GitInfo();
            }
        }
        catch
        {
            _gitInfo = new GitInfo();
        }
    }

    public static GitInfo GetGitInfo() => _gitInfo;

    public static string RenderFooterLine()
    {
        var info = GetGitInfo();
        if (info.CommitHash == "unknown")
        {
            return string.Empty;
        }

        var shortHash = info.CommitHash.Length > 8 ? info.CommitHash[..8] : info.CommitHash;
        var escapedMsg = System.Net.WebUtility.HtmlEncode(info.CommitMessage);
        
        return $"""
        <div class="git-info-footer" style="margin-top: 1rem; font-size: 0.8rem; color: var(--muted); border-top: 1px solid var(--border); padding-top: 1rem; text-align: center; width: 100%;">
          <span>Git Commit: </span>
          <span class="git-hash" style="font-family: monospace; color: var(--accent, #6366f1); cursor: help; border-bottom: 1px dashed var(--muted);" title="{escapedMsg}">
            {shortHash}
          </span>
          <span> ({info.CommitDate})</span>
        </div>
        """;
    }
}
