using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Cocoar.Configuration.Extensions.Providers.EnvironmentVariableProvider;

public sealed class EnvironmentVariableProvider(EnvironmentVariableProviderOptions options)
    : ConfigSourceProvider<EnvironmentVariableProviderOptions, EnvironmentVariableProviderQueryOptions>(options)
{
    public override Task<JsonElement?> GetValueAsync(EnvironmentVariableProviderQueryOptions queryOptions, CancellationToken ct = default)
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


    public override IObservable<ConfigChangeNotification> Changes(EnvironmentVariableProviderQueryOptions queryOptions)
    {
        // Emit the current value as both OldValue and NewValue, part set to "Environment"
        var value = GetValueAsync(queryOptions).GetAwaiter().GetResult();
        var notification = new ConfigChangeNotification(
            Part: "Environment",
            NewValue: value,
            OldValue: value
        );
        return System.Reactive.Linq.Observable.Return(notification);
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

    public static ConfigRule CreateRule<TConfigType>(string? memberPath = null)
    {
        var options = new EnvironmentVariableProviderOptions(memberPath);
        var queryOptions = new EnvironmentVariableProviderQueryOptions(memberPath);
        return ConfigRule.Create<EnvironmentVariableProvider, EnvironmentVariableProviderOptions, EnvironmentVariableProviderQueryOptions>(
            options,
            queryOptions,
            new ConfigTypeDefinition(typeof(TConfigType))
        );
    }
}
