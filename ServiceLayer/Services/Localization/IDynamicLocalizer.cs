namespace ServiceLayer.Services.Localization
{
    public interface IDynamicLocalizer
    {
        string this[string key, params object[] arguments] { get; }
        string GetString(string key, params object[] arguments);
    }
}
