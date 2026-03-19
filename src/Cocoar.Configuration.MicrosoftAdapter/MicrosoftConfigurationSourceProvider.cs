using System.Text.Json;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Providers.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Cocoar.Configuration.MicrosoftAdapter;

public sealed class MicrosoftConfigurationSourceProvider(
    MicrosoftConfigurationSourceProviderOptions options
) : ConfigurationProvider<MicrosoftConfigurationSourceProviderOptions,
    MicrosoftConfigurationSourceProviderQueryOptions>(options)
{
    private IConfigurationProvider BuildProvider()
    {
        var builder = new ConfigurationBuilder();
        if (!string.IsNullOrWhiteSpace(ProviderOptions.BasePath))
        {
            builder.SetBasePath(ProviderOptions.BasePath);
        }

        builder.Add(ProviderOptions.Source);
        return builder.Build().Providers.Last();
    }

    public override Task<byte[]> FetchConfigurationBytesAsync(MicrosoftConfigurationSourceProviderQueryOptions query,
        CancellationToken ct = default)
    {
        var provider = BuildProvider();
        var root = new ConfigurationRoot(new[] { provider });
        var dict = Flatten(root, query.ConfigurationPrefix);
        var bytes = DictToJsonBytes(dict);
        return Task.FromResult(bytes);
    }

    public override IObservable<byte[]> ChangesAsBytes(MicrosoftConfigurationSourceProviderQueryOptions query)
    {
        return new ChangeTokenObservable(this, query);
    }

    private static Dictionary<string, string?> Flatten(IConfigurationRoot root, string? ConfigurationPrefix)
    {
        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in root.AsEnumerable(makePathsRelative: false))
        {
            if (kv.Value is null || string.IsNullOrWhiteSpace(kv.Key))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(ConfigurationPrefix))
            {
                if (!kv.Key.StartsWith(ConfigurationPrefix + ":", StringComparison.OrdinalIgnoreCase)
                    && !kv.Key.Equals(ConfigurationPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var rel = kv.Key.Equals(ConfigurationPrefix, StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : kv.Key.Substring(ConfigurationPrefix.Length + 1);
                if (rel.Length == 0)
                {
                    continue;
                }

                dict[rel] = kv.Value;
            }
            else
            {
                dict[kv.Key] = kv.Value;
            }
        }

        return dict;
    }

    private static byte[] DictToJsonBytes(Dictionary<string, string?> flat)
    {
        var root = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in flat)
        {
            var parts = k.Split(':');
            var cur = root;
            for (var i = 0; i < parts.Length - 1; i++)
            {
                if (!cur.TryGetValue(parts[i], out var next) || next is not Dictionary<string, object?> nextDict)
                {
                    nextDict = new(StringComparer.OrdinalIgnoreCase);
                    cur[parts[i]] = nextDict;
                }

                cur = nextDict;
            }

            cur[parts[^1]] = v;
        }

        return JsonSerializer.SerializeToUtf8Bytes(root);
    }

    /// <summary>
    /// Helper method to create a Microsoft configuration source rule for testing purposes.
    /// </summary>
    public static Cocoar.Configuration.Rules.ConfigRule CreateRule<T>(
        Func<IConfigurationAccessor, MicrosoftConfigurationSourceRuleOptions> optionsFactory,
        bool required = false)
    {
        return Cocoar.Configuration.Rules.ConfigRule.Create<MicrosoftConfigurationSourceProvider,
            MicrosoftConfigurationSourceProviderOptions,
            MicrosoftConfigurationSourceProviderQueryOptions>(
            cm => optionsFactory(cm).ToProviderOptions(),
            cm => optionsFactory(cm).ToQueryOptions(),
            typeof(T),
            new Cocoar.Configuration.Rules.ConfigRuleOptions(Required: required, UseWhen: null)
        );
    }

    /// <summary>
    /// Wraps IChangeToken from a Microsoft configuration provider as an IObservable.
    /// Re-registers the change token after each callback (IChangeToken is single-fire).
    /// </summary>
    private sealed class ChangeTokenObservable(
        MicrosoftConfigurationSourceProvider owner,
        MicrosoftConfigurationSourceProviderQueryOptions query) : IObservable<byte[]>
    {
        public IDisposable Subscribe(IObserver<byte[]> observer)
        {
            var state = new ChangeTokenState(owner, query, observer);
            state.Register();
            return state;
        }

        private sealed class ChangeTokenState : IDisposable
        {
            private readonly MicrosoftConfigurationSourceProvider _owner;
            private readonly MicrosoftConfigurationSourceProviderQueryOptions _query;
            private readonly IObserver<byte[]> _observer;
            private readonly IConfigurationProvider _provider;
            private IDisposable? _registration;
            private int _disposed;

            public ChangeTokenState(
                MicrosoftConfigurationSourceProvider owner,
                MicrosoftConfigurationSourceProviderQueryOptions query,
                IObserver<byte[]> observer)
            {
                _owner = owner;
                _query = query;
                _observer = observer;
                _provider = owner.BuildProvider();
            }

            public void Register()
            {
                if (Volatile.Read(ref _disposed) != 0) return;
                var token = _provider.GetReloadToken();
                _registration = token.RegisterChangeCallback(_ => OnChange(), null);
            }

            private void OnChange()
            {
                if (Volatile.Read(ref _disposed) != 0) return;

                var root = new ConfigurationRoot(new[] { _provider });
                var dict = Flatten(root, _query.ConfigurationPrefix);
                var bytes = DictToJsonBytes(dict);
                try { _observer.OnNext(bytes); } catch { /* observer fault must not break re-registration */ }

                // IChangeToken is single-fire — re-register for the next change
                Register();
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
                _registration?.Dispose();
            }
        }
    }
}
