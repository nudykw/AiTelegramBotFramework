namespace ServiceLayer.Services.Localization;

/// <summary>
/// A no-op localizer that returns the key itself as the value.
/// Used for fallback localization when a real localizer is not available.
/// </summary>
public class NullLocalizer : IDynamicLocalizer
{
    public string this[string key, params object[] arguments] => Format(key, arguments);

    public string GetString(string key, params object[] arguments) => Format(key, arguments);

    private static string Format(string template, object[] arguments)
    {
        if (arguments == null || arguments.Length == 0) return template;
        try
        {
            return string.Format(template, arguments);
        }
        catch
        {
            return template;
        }
    }
}
