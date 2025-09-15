using System.Text;
using System.Text.Json;
using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Providers.EnvironmentVariableProvider;

public sealed class EnvironmentVariableProvider(EnvironmentVariableProviderOptions options)
    : ConfigurationProvider<EnvironmentVariableProviderOptions, EnvironmentVariableProviderQueryOptions>(options)
{
    public override Task<JsonElement> FetchConfigurationAsync(EnvironmentVariableProviderQueryOptions queryOptions,
        CancellationToken ct = default)
    {
        var prefix = queryOptions.EnvironmentPrefix;
        var variables = Environment.GetEnvironmentVariables();
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var keyObj in variables.Keys)
        {
            var key = keyObj.ToString()!;
            if (!string.IsNullOrEmpty(prefix))
            {
                if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                AddToNestedDict(dict, key.Substring(prefix.Length), variables[keyObj]);
            }
            else
            {
                AddToNestedDict(dict, key, variables[keyObj]);
            }
        }

        var json = JsonSerializer.Serialize(dict);
        using var doc = JsonDocument.Parse(json);
        var element = doc.RootElement.Clone();

        return Task.FromResult(element);
    }

    private static void AddToNestedDict(IDictionary<string, object?> dict, string key, object? value)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        // Trim a single leading separator (for prefix cases like "MYAPP" + "_FOO")
        key = TrimSingleLeadingSeparator(key);

        var parts = SplitEnvKey(key).ToArray();
        if (parts.Length == 0)
            return;

        var current = dict;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            var seg = parts[i];
            if (!current.TryGetValue(seg, out var next) || next is not IDictionary<string, object?> nextDict)
            {
                nextDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                current[seg] = nextDict;
            }

            current = nextDict;
        }

        current[parts[^1]] = value;
    }

    // Split using .NET convention: "__" is a nesting separator (like ':'), and '.' is also treated as a separator.
    // Single '_' is literal and NOT a separator.
    private static IEnumerable<string> SplitEnvKey(string key)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < key.Length;)
        {
            char c = key[i];

            // Colon is a nesting separator (Microsoft convention)
            if (c == ':')
            {
                if (sb.Length > 0)
                {
                    yield return sb.ToString();
                    sb.Clear();
                }

                i++;
                continue;
            }

            // Double underscore (or run of >=2 underscores) is a separator
            if (c == '_' && i + 1 < key.Length && key[i + 1] == '_')
            {
                // Consume the entire run of underscores
                int j = i;
                while (j < key.Length && key[j] == '_') j++;
                if (j - i >= 2)
                {
                    if (sb.Length > 0)
                    {
                        yield return sb.ToString();
                        sb.Clear();
                    }

                    i = j;
                    continue;
                }
            }

            // Otherwise, literal character
            sb.Append(c);
            i++;
        }

        if (sb.Length > 0)
            yield return sb.ToString();
    }

    private static string TrimSingleLeadingSeparator(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        // If starts with double underscore, treat as delimiter and remove it.
        if (s.Length >= 2 && s[0] == '_' && s[1] == '_')
            return s[2..];
        // Otherwise, trim a single leading ':' or '_' if present
        if (s[0] == ':' || s[0] == '_')
            return s[1..];
        return s;
    }


    public override IObservable<JsonElement> Changes(EnvironmentVariableProviderQueryOptions queryOptions)
        => System.Reactive.Linq.Observable.Never<JsonElement>();
}
