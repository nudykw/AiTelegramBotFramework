using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ServiceLayer.Utils;

/// <summary>
/// Base implementation for static string enums (multitons).
/// </summary>
/// <typeparam name="TSelf">The concrete subclass type.</typeparam>
[JsonConverter(typeof(StaticStringEnumJsonConverterFactory))]
public abstract class StaticStringEnumBase<TSelf> : IStaticStringEnum<TSelf> 
    where TSelf : StaticStringEnumBase<TSelf>, IStaticStringEnum<TSelf>
{
    private static readonly Lazy<IEnumerable<TSelf>> _allInstances = new(() => 
        typeof(TSelf)
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(TSelf))
            .Select(f => (TSelf?)f.GetValue(null))
            .Where(v => v != null)
            .Cast<TSelf>()
            .ToList());

    protected StaticStringEnumBase(string value)
    {
        Value = value;
    }

    public string Value { get; }

    // We do not define DefaultIgnoreCase as static here,
    // so that it is retrieved from the IStaticStringEnum<TSelf> interface.

    public static IEnumerable<TSelf> GetAll() => _allInstances.Value;

    public static bool IsValid(string? value, bool? ignoreCase = null)
    {
        return FromString(value, ignoreCase) != null;
    }

    public static TSelf? FromString(string? value, bool? ignoreCase = null)
    {
        if (value == null) return null;
        
        bool useIgnoreCase = ignoreCase ?? TSelf.DefaultIgnoreCase;

        var comp = useIgnoreCase 
            ? StringComparison.OrdinalIgnoreCase 
            : StringComparison.Ordinal;

        return GetAll().FirstOrDefault(i => string.Equals(i.Value, value, comp));
    }

    public static TSelf Parse(string s, IFormatProvider? provider = null)
    {
        return FromString(s) ?? throw new FormatException($"Invalid value for {typeof(TSelf).Name}: {s}");
    }

    public static bool TryParse(string? value, out TSelf? result)
    {
        result = FromString(value);
        return result != null;
    }

    public static bool TryParse(string? value, IFormatProvider? provider, out TSelf? result)
    {
        return TryParse(value, out result);
    }

    private static bool GetDefaultIgnoreCase() => TSelf.DefaultIgnoreCase;

    // Implicit conversion to string
    public static implicit operator string(StaticStringEnumBase<TSelf>? instance) => instance?.Value ?? string.Empty;

    // Implicit conversion from string (returns the base type)
    public static implicit operator StaticStringEnumBase<TSelf>?(string? value) => FromString(value);

    // Comparison operators
    public static bool operator ==(StaticStringEnumBase<TSelf>? left, StaticStringEnumBase<TSelf>? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        return left.Equals(right);
    }

    public static bool operator !=(StaticStringEnumBase<TSelf>? left, StaticStringEnumBase<TSelf>? right) => !(left == right);

    public override string ToString() => Value;

    public override bool Equals(object? obj)
    {
        if (obj is StaticStringEnumBase<TSelf> other)
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        return false;
    }

    public override int GetHashCode() => Value.GetHashCode();
}

/// <summary>
/// TypeConverter to support ConfigurationBinder and other .NET mechanisms.
/// Should be applied to the concrete subclass: [TypeConverter(typeof(StaticStringEnumTypeConverter&lt;T&gt;))]
/// </summary>
public class StaticStringEnumTypeConverter<T> : TypeConverter where T : StaticStringEnumBase<T>, IStaticStringEnum<T>
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object? value)
    {
        if (value is string s)
        {
            return T.FromString(s);
        }
        return base.ConvertFrom(context, culture, value);
    }
}

/// <summary>
/// Factory to create JsonConverter for any subclasses of StaticStringEnumBase.
/// </summary>
public class StaticStringEnumJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(StaticStringEnumBase<>) 
            || (typeToConvert.BaseType?.IsGenericType == true && typeToConvert.BaseType.GetGenericTypeDefinition() == typeof(StaticStringEnumBase<>));
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        Type tSelf = typeToConvert;
        Type converterType = typeof(StaticStringEnumJsonConverter<>).MakeGenericType(tSelf);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}

/// <summary>
/// JsonConverter for a concrete TSelf type.
/// </summary>
public class StaticStringEnumJsonConverter<TSelf> : JsonConverter<TSelf> 
    where TSelf : StaticStringEnumBase<TSelf>, IStaticStringEnum<TSelf>
{
    public override TSelf? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? value = reader.GetString();
        return TSelf.FromString(value);
    }

    public override void Write(Utf8JsonWriter writer, TSelf value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}
