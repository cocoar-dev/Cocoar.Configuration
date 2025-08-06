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
                if (!key.StartsWith(prefix + "_", StringComparison.OrdinalIgnoreCase))
                    continue;
                dict[key.Substring(prefix.Length + 1)] = variables[keyObj];
            }
            else
            {
                dict[key] = variables[keyObj];
            }
        }

        var json = JsonSerializer.Serialize(dict);
        var doc = JsonDocument.Parse(json);
        var element = doc.RootElement.Clone();

        // Use the base class helper to wrap if needed
        return Task.FromResult(WrapIfNeeded(element, queryOptions.MemberWrapper));
    }


    public override IObservable<JsonElement> Changes(EnvironmentVariableProviderQueryOptions queryOptions)
    {
        var value = GetValueAsync(queryOptions).GetAwaiter().GetResult();
        return System.Reactive.Linq.Observable.Return(value);
    }

    public static ConfigRule CreateRule<TConfigType, TImplementationType>(string? memberPath = null, Func<bool>? useWhen = null)
    {
        var options = new EnvironmentVariableProviderOptions(memberPath);
        var queryOptions = new EnvironmentVariableProviderQueryOptions(memberPath);
        
        return ConfigRule.Create<EnvironmentVariableProvider, EnvironmentVariableProviderOptions, EnvironmentVariableProviderQueryOptions>(
            options,
            queryOptions,
            new ConfigTypeDefinition(typeof(TConfigType), typeof(TImplementationType)),
            useWhen: useWhen
        );
    }

    public static ConfigRule CreateRule<TConfigType>(string? memberPath = null, Func<bool>? useWhen = null)
    {
        var options = new EnvironmentVariableProviderOptions(memberPath);
        var queryOptions = new EnvironmentVariableProviderQueryOptions(memberPath);
        return ConfigRule.Create<EnvironmentVariableProvider, EnvironmentVariableProviderOptions, EnvironmentVariableProviderQueryOptions>(
            options,
            queryOptions,
            new ConfigTypeDefinition(typeof(TConfigType)),
            useWhen
        );
    }
}
