using System.Text.Json;

namespace Cocoar.Configuration.Utilities;

/// <summary>
/// Utility class for JSON configuration processing operations like flattening and unflattening.
/// </summary>
internal static class JsonConfigurationProcessor
{
    /// <summary>
    /// Flattens a JSON element into a dictionary with colon-separated keys.
    /// </summary>
    public static Dictionary<string, JsonElement> Flatten(JsonElement element)
    {
        var dict = new Dictionary<string, JsonElement>();
        FlattenRecursive(element, null, dict);
        return dict;
    }

    /// <summary>
    /// Reconstructs a hierarchical JSON element from a flattened dictionary.
    /// </summary>
    public static JsonElement Unflatten(Dictionary<string, JsonElement> flat)
    {
        var root = new Dictionary<string, object>();

        foreach (var kvp in flat)
        {
            var keys = kvp.Key.Split(':');
            var current = root;

            for (int i = 0; i < keys.Length; i++)
            {
                var key = keys[i];
                if (i == keys.Length - 1)
                {
                    // Last key - set the value
                    current[key] = kvp.Value;
                }
                else
                {
                    // Intermediate key - ensure nested dictionary exists
                    if (!current.TryGetValue(key, out var next) || next is not Dictionary<string, object>)
                    {
                        next = new Dictionary<string, object>();
                        current[key] = next;
                    }

                    current = (Dictionary<string, object>)next;
                }
            }
        }

        var json = JsonSerializer.Serialize(root);
        return JsonDocument.Parse(json).RootElement;
    }

    private static void FlattenRecursive(JsonElement e, string? prefix, Dictionary<string, JsonElement> dict)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in e.EnumerateObject())
            {
                var key = prefix == null ? prop.Name : $"{prefix}:{prop.Name}";
                FlattenRecursive(prop.Value, key, dict);
            }
        }
        else if (prefix != null)
        {
            dict[prefix] = e;
        }
    }
}
