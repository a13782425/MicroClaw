using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Configuration;
using YamlDotNet.Serialization;

namespace MicroClaw.Configuration;

/// <summary>
/// Binds an <see cref="IConfiguration"/> section onto a strongly typed options instance,
/// resolving each property's YAML key from <see cref="YamlMemberAttribute.Alias"/> when present,
/// otherwise from an UnderscoredNamingConvention-compatible snake_case conversion of the property name.
/// <para>
/// This replaces the default <c>ConfigurationBinder</c> / <c>ConfigurationKeyName</c> pair so that a single
/// YAML-oriented attribute (<c>[YamlMember]</c>) is the source of truth for both serialization and binding.
/// </para>
/// </summary>
internal static class YamlAwareBinder
{
    private static readonly ConcurrentDictionary<Type, PropertyBinding[]> PropertyCache = new();
    private static readonly ConcurrentDictionary<string, string> SnakeCaseCache = new(StringComparer.Ordinal);

    public static void Bind(IConfiguration section, object instance)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(instance);

        foreach (PropertyBinding binding in GetBindings(instance.GetType()))
        {
            IConfigurationSection child = section.GetSection(binding.Key);
            if (!child.Exists())
                continue;

            object? current = binding.Getter?.Invoke(instance);
            object? next = BindValue(child, binding.Property.PropertyType, current);
            binding.Setter(instance, next);
        }
    }

    private static object? BindValue(IConfigurationSection section, Type targetType, object? currentValue)
    {
        Type underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (IsScalarType(underlying))
        {
            string? raw = section.Value;
            if (raw is null)
                return currentValue;
            return ConvertScalar(raw, underlying);
        }

        if (TryGetEnumerableElementType(underlying, out Type? elementType))
            return BindList(section, underlying, elementType!);

        object nested = currentValue ?? Activator.CreateInstance(underlying)
            ?? throw new InvalidOperationException($"无法为类型 {underlying.FullName} 创建实例。");
        Bind(section, nested);
        return nested;
    }

    private static object BindList(IConfigurationSection section, Type listType, Type elementType)
    {
        Type concreteList = typeof(List<>).MakeGenericType(elementType);
        IList list = (IList)Activator.CreateInstance(concreteList)!;

        IEnumerable<IConfigurationSection> ordered = section.GetChildren()
            .OrderBy(static c => int.TryParse(c.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) ? i : int.MaxValue);

        Type elementUnderlying = Nullable.GetUnderlyingType(elementType) ?? elementType;
        foreach (IConfigurationSection child in ordered)
        {
            object? element;
            if (IsScalarType(elementUnderlying))
            {
                element = child.Value is null ? null : ConvertScalar(child.Value, elementUnderlying);
            }
            else
            {
                element = Activator.CreateInstance(elementUnderlying)
                    ?? throw new InvalidOperationException($"无法为集合元素类型 {elementUnderlying.FullName} 创建实例。");
                Bind(child, element);
            }

            list.Add(element);
        }

        if (listType.IsArray)
        {
            Array array = Array.CreateInstance(elementType, list.Count);
            list.CopyTo(array, 0);
            return array;
        }

        return list;
    }

    private static bool IsScalarType(Type type)
    {
        if (type.IsPrimitive) return true;
        if (type.IsEnum) return true;
        if (type == typeof(string)) return true;
        if (type == typeof(decimal)) return true;
        if (type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan) || type == typeof(Guid))
            return true;
        return false;
    }

    private static bool TryGetEnumerableElementType(Type type, out Type? elementType)
    {
        if (type.IsArray)
        {
            elementType = type.GetElementType();
            return elementType is not null;
        }

        if (type.IsGenericType)
        {
            Type def = type.GetGenericTypeDefinition();
            if (def == typeof(List<>) ||
                def == typeof(IList<>) ||
                def == typeof(ICollection<>) ||
                def == typeof(IEnumerable<>) ||
                def == typeof(IReadOnlyList<>) ||
                def == typeof(IReadOnlyCollection<>))
            {
                elementType = type.GetGenericArguments()[0];
                return true;
            }
        }

        elementType = null;
        return false;
    }

    private static object ConvertScalar(string raw, Type targetType)
    {
        if (targetType == typeof(string)) return raw;
        if (targetType.IsEnum) return Enum.Parse(targetType, raw, ignoreCase: true);
        if (targetType == typeof(bool)) return bool.Parse(raw);
        if (targetType == typeof(int)) return int.Parse(raw, CultureInfo.InvariantCulture);
        if (targetType == typeof(long)) return long.Parse(raw, CultureInfo.InvariantCulture);
        if (targetType == typeof(short)) return short.Parse(raw, CultureInfo.InvariantCulture);
        if (targetType == typeof(byte)) return byte.Parse(raw, CultureInfo.InvariantCulture);
        if (targetType == typeof(float)) return float.Parse(raw, CultureInfo.InvariantCulture);
        if (targetType == typeof(double)) return double.Parse(raw, CultureInfo.InvariantCulture);
        if (targetType == typeof(decimal)) return decimal.Parse(raw, CultureInfo.InvariantCulture);
        if (targetType == typeof(Guid)) return Guid.Parse(raw);
        if (targetType == typeof(DateTime)) return DateTime.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (targetType == typeof(DateTimeOffset)) return DateTimeOffset.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (targetType == typeof(TimeSpan)) return TimeSpan.Parse(raw, CultureInfo.InvariantCulture);

        return Convert.ChangeType(raw, targetType, CultureInfo.InvariantCulture)!;
    }

    private static PropertyBinding[] GetBindings(Type type)
    {
        return PropertyCache.GetOrAdd(type, BuildBindings);
    }

    private static PropertyBinding[] BuildBindings(Type type)
    {
        List<PropertyBinding> bindings = [];

        foreach (PropertyInfo prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetIndexParameters().Length > 0) continue;
            if (!prop.CanWrite) continue;

            YamlMemberAttribute? yamlAttr = prop.GetCustomAttribute<YamlMemberAttribute>(inherit: true);
            string key = yamlAttr is not null && !string.IsNullOrWhiteSpace(yamlAttr.Alias)
                ? yamlAttr.Alias
                : ToSnakeCase(prop.Name);

            Action<object, object?> setter = (target, value) => prop.SetValue(target, value);
            Func<object, object?>? getter = prop.CanRead ? target => prop.GetValue(target) : null;

            bindings.Add(new PropertyBinding(prop, key, setter, getter));
        }

        return [.. bindings];
    }

    /// <summary>
    /// Converts a PascalCase / camelCase identifier to snake_case, matching
    /// <see cref="YamlDotNet.Serialization.NamingConventions.UnderscoredNamingConvention"/>.
    /// Inserts '_' before each uppercase character that follows a lowercase, digit, or another uppercase
    /// followed by a lowercase (handles runs like <c>HTTPSOption</c> → <c>https_option</c>).
    /// </summary>
    internal static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        return SnakeCaseCache.GetOrAdd(name, static value =>
        {
            StringBuilder sb = new(value.Length + 8);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (i > 0 && char.IsUpper(c))
                {
                    char prev = value[i - 1];
                    bool prevIsLowerOrDigit = char.IsLower(prev) || char.IsDigit(prev);
                    bool acronymBoundary = char.IsUpper(prev)
                        && i + 1 < value.Length
                        && char.IsLower(value[i + 1]);
                    if (prevIsLowerOrDigit || acronymBoundary)
                        sb.Append('_');
                }
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        });
    }

    private sealed record PropertyBinding(
        PropertyInfo Property,
        string Key,
        Action<object, object?> Setter,
        Func<object, object?>? Getter);
}
