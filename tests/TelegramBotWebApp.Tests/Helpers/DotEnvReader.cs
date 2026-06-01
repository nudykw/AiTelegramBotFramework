namespace TelegramBotWebApp.Tests.Helpers;

/// <summary>
/// Reads key-value pairs from a <c>.env</c> file using the same format that
/// Docker Compose expects: one <c>KEY=value</c> per line, comments start with <c>#</c>.
/// Variable substitution (e.g. <c>${VAR}</c>) is intentionally NOT performed —
/// tests only read literal values.
/// </summary>
public static class DotEnvReader
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> _values =
        new(() => Load(FindEnvFile()));

    /// <summary>Lazily loaded values from the nearest <c>.env</c> file.</summary>
    public static IReadOnlyDictionary<string, string> Values => _values.Value;

    /// <summary>
    /// Returns the value for <paramref name="key"/> from the <c>.env</c> file,
    /// or <see langword="null"/> when the key is absent or the file does not exist.
    /// </summary>
    public static string? Get(string key)
        => Values.TryGetValue(key, out var v) ? v : null;

    /// <summary>
    /// Returns <see langword="true"/> when <c>TELEGRAM_BASE_API_URL</c> is set
    /// to a non-empty value, meaning the bot is configured for Webhook mode.
    /// </summary>
    public static bool IsWebhookMode =>
        !string.IsNullOrWhiteSpace(Get("TELEGRAM_BASE_API_URL"));

    // ── Private helpers ───────────────────────────────────────────────────────

    private static IReadOnlyDictionary<string, string> Load(string? path)
    {
        if (path is null || !File.Exists(path))
            return new Dictionary<string, string>();

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();

            // Skip comments and lines without an equals sign
            if (line.StartsWith('#') || line.Length == 0 || !line.Contains('='))
                continue;

            var eqIdx = line.IndexOf('=');
            var key   = line[..eqIdx].Trim();
            var value = line[(eqIdx + 1)..].Trim();

            // Strip optional surrounding quotes (" or ')
            if (value.Length >= 2
                && ((value[0] == '"'  && value[^1] == '"')
                 || (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }

            if (!string.IsNullOrEmpty(key))
                result[key] = value;
        }

        return result;
    }

    /// <summary>
    /// Walks parent directories from the test binary's <see cref="AppContext.BaseDirectory"/>
    /// until it finds a <c>.env</c> file (the solution root).
    /// </summary>
    private static string? FindEnvFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, ".env");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
