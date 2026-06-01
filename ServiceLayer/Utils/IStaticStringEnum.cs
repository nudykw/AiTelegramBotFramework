namespace ServiceLayer.Utils;

/// <summary>
/// Interface for static string enums (multitons).
/// </summary>
/// <typeparam name="TSelf">The type of the concrete enum class.</typeparam>
public interface IStaticStringEnum<TSelf> where TSelf : class, IStaticStringEnum<TSelf>
{
    /// <summary>
    /// Text value of the constant.
    /// </summary>
    string Value { get; }

    /// <summary>
    /// Returns a list of all available fields (instances) in the class.
    /// </summary>
    static abstract IEnumerable<TSelf> GetAll();

    /// <summary>
    /// Default comparison strategy (case sensitivity).
    /// </summary>
    static virtual bool DefaultIgnoreCase => false;

    /// <summary>
    /// Checks if the text value is one of the valid fields of the class.
    /// </summary>
    static abstract bool IsValid(string? value, bool? ignoreCase = null);

    /// <summary>
    /// Converts a string to an instance of the class. Returns null if no match is found.
    /// </summary>
    static abstract TSelf? FromString(string? value, bool? ignoreCase = null);
}
