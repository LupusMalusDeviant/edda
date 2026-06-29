using System.Text.Json;

namespace Edda.Agent.Tools;

/// <summary>
/// Helper methods for extracting typed values from tool call argument dictionaries.
/// Handles both raw CLR types and <see cref="JsonElement"/> values produced by
/// System.Text.Json deserialization of the model's JSON tool-call arguments.
/// </summary>
internal static class ToolArgumentHelper
{
    /// <summary>
    /// Tries to retrieve a string value from the argument dictionary.
    /// </summary>
    /// <param name="args">The tool call arguments dictionary.</param>
    /// <param name="key">The argument key to look up.</param>
    /// <returns>The string value, or <c>null</c> if the key is absent or not a string.</returns>
    public static string? GetString(IReadOnlyDictionary<string, object?> args, string key)
    {
        if (!args.TryGetValue(key, out var value)) return null;
        if (value is string s) return s;
        if (value is JsonElement je && je.ValueKind == JsonValueKind.String)
            return je.GetString();
        return value?.ToString();
    }

    /// <summary>
    /// Tries to retrieve an integer value from the argument dictionary.
    /// </summary>
    /// <param name="args">The tool call arguments dictionary.</param>
    /// <param name="key">The argument key to look up.</param>
    /// <returns>The integer value, or <c>null</c> if the key is absent or not numeric.</returns>
    public static int? GetInt(IReadOnlyDictionary<string, object?> args, string key)
    {
        if (!args.TryGetValue(key, out var value)) return null;
        if (value is int i) return i;
        if (value is long l) return (int)l;
        if (value is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Number && je.TryGetInt32(out var n))
                return n;
        }
        if (value is string str && int.TryParse(str, out var parsed))
            return parsed;
        return null;
    }

    /// <summary>
    /// Retrieves a required string argument. Returns the value trimmed of whitespace.
    /// </summary>
    /// <param name="args">The tool call arguments dictionary.</param>
    /// <param name="key">The argument key to look up.</param>
    /// <returns>The non-empty string value.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the key is absent or the value is null/empty/whitespace.
    /// </exception>
    public static string GetRequiredString(IReadOnlyDictionary<string, object?> args, string key)
    {
        var value = GetString(args, key);
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"Required argument '{key}' is missing or empty.", key);
        return value.Trim();
    }

    /// <summary>
    /// Tries to retrieve a nested dictionary (object) from the argument dictionary.
    /// </summary>
    /// <param name="args">The tool call arguments dictionary.</param>
    /// <param name="key">The argument key to look up.</param>
    /// <returns>
    /// A dictionary of string keys to string? values, or an empty dictionary
    /// if the key is absent or the value is not a JSON object.
    /// </returns>
    public static IReadOnlyDictionary<string, string?> GetDictionary(
        IReadOnlyDictionary<string, object?> args,
        string key)
    {
        if (!args.TryGetValue(key, out var value))
            return new Dictionary<string, string?>();

        if (value is IReadOnlyDictionary<string, string?> rdict) return rdict;
        if (value is IDictionary<string, object?> odict)
        {
            return odict.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString());
        }
        if (value is JsonElement je && je.ValueKind == JsonValueKind.Object)
        {
            return je.EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.ValueKind == JsonValueKind.String
                    ? p.Value.GetString()
                    : p.Value.ToString());
        }
        return new Dictionary<string, string?>();
    }

    /// <summary>
    /// Tries to retrieve a boolean value from the argument dictionary.
    /// Handles CLR <see cref="bool"/>, <see cref="JsonElement"/> true/false literals,
    /// and string representations ("true"/"false", case-insensitive).
    /// </summary>
    /// <param name="args">The tool call arguments dictionary.</param>
    /// <param name="key">The argument key to look up.</param>
    /// <param name="defaultValue">Value returned when the key is absent or unrecognised.</param>
    /// <returns>The boolean value, or <paramref name="defaultValue"/> if the key is absent or not parseable.</returns>
    public static bool GetBool(IReadOnlyDictionary<string, object?> args, string key, bool defaultValue = false)
    {
        if (!args.TryGetValue(key, out var value)) return defaultValue;
        if (value is bool b) return b;
        if (value is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.True)  return true;
            if (je.ValueKind == JsonValueKind.False) return false;
            if (je.ValueKind == JsonValueKind.String &&
                bool.TryParse(je.GetString(), out var parsed))
                return parsed;
        }
        if (value is string str && bool.TryParse(str, out var result))
            return result;
        return defaultValue;
    }

    /// <summary>
    /// Tries to retrieve a JSON object array from the argument dictionary.
    /// Non-object elements within the array are silently skipped.
    /// </summary>
    /// <param name="args">The tool call arguments dictionary.</param>
    /// <param name="key">The argument key to look up.</param>
    /// <returns>
    /// An array of <see cref="JsonElement"/> values of kind <see cref="JsonValueKind.Object"/>,
    /// or an empty array when the key is absent or the value is not a JSON array.
    /// </returns>
    public static JsonElement[] GetObjectArray(IReadOnlyDictionary<string, object?> args, string key)
    {
        if (!args.TryGetValue(key, out var value)) return [];
        if (value is JsonElement je && je.ValueKind == JsonValueKind.Array)
            return je.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.Object)
                .ToArray();
        return [];
    }

    /// <summary>
    /// Tries to retrieve a string array from the argument dictionary.
    /// Returns an empty array if the key is absent or the value is not a JSON array.
    /// Null, empty, and whitespace-only elements are silently skipped.
    /// </summary>
    /// <param name="args">The tool call arguments dictionary.</param>
    /// <param name="key">The argument key to look up.</param>
    /// <returns>An array of non-empty strings, or an empty array when the key is absent or not a JSON array.</returns>
    public static string[] GetStringArray(IReadOnlyDictionary<string, object?> args, string key)
    {
        if (!args.TryGetValue(key, out var value)) return [];
        if (value is string[] sa) return sa;
        if (value is IEnumerable<string> se) return se.ToArray();
        if (value is JsonElement je && je.ValueKind == JsonValueKind.Array)
            return je.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToArray();
        return [];
    }
}
