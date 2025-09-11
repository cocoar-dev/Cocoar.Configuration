using System.Text.Json;
using Cocoar.Configuration.Providers.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Cocoar.Configuration.MicrosoftAdapter;

public sealed class MicrosoftConfigurationSourceProvider(
    MicrosoftConfigurationSourceProviderOptions options
) : ConfigurationProvider<MicrosoftConfigurationSourceProviderOptions, MicrosoftConfigurationSourceProviderQueryOptions>(options)
{
    private IConfigurationProvider BuildProvider()
    {
        var builder = new ConfigurationBuilder();
        if (!string.IsNullOrWhiteSpace(ProviderOptions.BasePath))
            builder.SetBasePath(ProviderOptions.BasePath);
        builder.Add(ProviderOptions.Source);
        return builder.Build().Providers.Last();
    }

    public override Task<JsonElement> FetchConfigurationAsync(MicrosoftConfigurationSourceProviderQueryOptions query, CancellationToken ct = default)
    {
        var provider = BuildProvider();
        var root = new ConfigurationRoot(new[] { provider });
        var dict = Flatten(root, query.ConfigurationPrefix);
        var element = DictToJson(dict);
        return Task.FromResult(WrapIfNeeded(element, query.TargetPath));
    }

    public override IObservable<JsonElement> Changes(MicrosoftConfigurationSourceProviderQueryOptions query)
    {
        return System.Reactive.Linq.Observable.Create<JsonElement>(observer =>
        {
            var provider = BuildProvider();
            void publish()
            {
                var root = new ConfigurationRoot(new[] { provider });
                var dict = Flatten(root, query.ConfigurationPrefix);
                var json = WrapIfNeeded(DictToJson(dict), query.TargetPath);
                observer.OnNext(json);
            }

            var token = provider.GetReloadToken();
            var reg = token.RegisterChangeCallback(_ => publish(), null);
            return System.Reactive.Disposables.Disposable.Create(() => reg.Dispose());
        });
    }

    private static Dictionary<string, string?> Flatten(IConfigurationRoot root, string? ConfigurationPrefix)
    {
        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in root.AsEnumerable(makePathsRelative: false))
        {
            if (kv.Value is null || string.IsNullOrWhiteSpace(kv.Key)) continue;
            if (!string.IsNullOrWhiteSpace(ConfigurationPrefix))
            {
                if (!kv.Key.StartsWith(ConfigurationPrefix + ":", StringComparison.OrdinalIgnoreCase)
                    && !kv.Key.Equals(ConfigurationPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                var rel = kv.Key.Equals(ConfigurationPrefix, StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : kv.Key.Substring(ConfigurationPrefix.Length + 1);
                if (rel.Length == 0) continue;
                dict[rel] = kv.Value;
            }
            else
            {
                dict[kv.Key] = kv.Value;
            }
        }
        return dict;
    }

    private static JsonElement DictToJson(Dictionary<string, string?> flat)
    {
        var root = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in flat)
        {
            var parts = k.Split(':');
            var cur = root;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (!cur.TryGetValue(parts[i], out var next) || next is not Dictionary<string, object?> nextDict)
                {
                    nextDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    cur[parts[i]] = nextDict;
                }
                cur = nextDict;
            }
            cur[parts[^1]] = v;
        }
        var json = JsonSerializer.Serialize(root);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
