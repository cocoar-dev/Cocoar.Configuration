using System.Text.Json;

namespace Cocoar.Configuration.Providers.EnvironmentVariableProvider;

public sealed class EnvironmentVariableProvider(EnvironmentVariableProviderOptions options)
    : ConfigSourceProvider<EnvironmentVariableProviderOptions, EnvironmentVariableProviderQueryOptions>(options)
{
    public override Task<JsonElement> GetValueAsync(EnvironmentVariableProviderQueryOptions queryOptions, CancellationToken ct = default)
    {
        var prefix = queryOptions.MemberPath;
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
        var doc = JsonDocument.Parse(json);
        var element = doc.RootElement.Clone();

        // Use the base class helper to wrap if needed
        return Task.FromResult(WrapIfNeeded(element, queryOptions.MemberWrapper));
    }

    private static void AddToNestedDict(IDictionary<string, object?> dict, string key, object? value)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        var parts = key.Split(new[] { ':', '_', '.' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return;
        var current = dict;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (!current.TryGetValue(parts[i], out var next) || next is not IDictionary<string, object?> nextDict)
            {
                nextDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                current[parts[i]] = nextDict;
            }
            current = nextDict;
        }
        current[parts[^1]] = value;
    }


    public override IObservable<JsonElement> Changes(EnvironmentVariableProviderQueryOptions queryOptions)
        => System.Reactive.Linq.Observable.Never<JsonElement>();

    public static ConfigRule CreateRule<TConfigType, TImplementationType>(string? memberPath = null, Func<bool>? useWhen = null, bool required = false)
    {
        var options = new EnvironmentVariableProviderOptions(memberPath);
        var queryOptions = new EnvironmentVariableProviderQueryOptions(memberPath);
        
        return ConfigRule.Create<EnvironmentVariableProvider, EnvironmentVariableProviderOptions, EnvironmentVariableProviderQueryOptions>(
            options,
            queryOptions,
            new ConfigTypeDefinition(typeof(TConfigType), typeof(TImplementationType)),
            useWhen: useWhen,
            required: required
        );
    }

    public static ConfigRule CreateRule<TConfigType>(string? memberPath = null, Func<bool>? useWhen = null, bool required = false)
    {
        var options = new EnvironmentVariableProviderOptions(memberPath);
        var queryOptions = new EnvironmentVariableProviderQueryOptions(memberPath);
        return ConfigRule.Create<EnvironmentVariableProvider, EnvironmentVariableProviderOptions, EnvironmentVariableProviderQueryOptions>(
            options,
            queryOptions,
            new ConfigTypeDefinition(typeof(TConfigType)),
            useWhen,
            required
        );
    }

    public static ConfigRule CreateRule<TConfigType>(
        Func<ConfigManager, string?> memberPath,
        Func<bool>? useWhen = null,
        bool required = false)
    {
        return ConfigRule.Create<EnvironmentVariableProvider, EnvironmentVariableProviderOptions, EnvironmentVariableProviderQueryOptions>(
            cm => new EnvironmentVariableProviderOptions(memberPath(cm)),
            cm => new EnvironmentVariableProviderQueryOptions(memberPath(cm)),
            new ConfigTypeDefinition(typeof(TConfigType)),
            useWhen,
            required
        );
    }

    public static ConfigRule CreateRule<TConfigType, TImplementationType>(
        Func<ConfigManager, string?> memberPath,
        Func<bool>? useWhen = null,
        bool required = false)
    {
        return ConfigRule.Create<EnvironmentVariableProvider, EnvironmentVariableProviderOptions, EnvironmentVariableProviderQueryOptions>(
            cm => new EnvironmentVariableProviderOptions(memberPath(cm)),
            cm => new EnvironmentVariableProviderQueryOptions(memberPath(cm)),
            new ConfigTypeDefinition(typeof(TConfigType), typeof(TImplementationType)),
            useWhen,
            required
        );
    }
}
