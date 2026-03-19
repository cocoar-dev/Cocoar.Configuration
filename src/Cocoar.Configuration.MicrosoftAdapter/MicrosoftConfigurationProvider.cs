using System.Text.Json;
using Cocoar.Configuration.Providers.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace Cocoar.Configuration.MicrosoftAdapter;

/// <summary>
/// Configuration provider that reads from an existing <see cref="IConfiguration"/> instance.
/// Supports <see cref="IConfigurationRoot"/>, <see cref="IConfigurationSection"/>, and any
/// <see cref="IConfiguration"/> implementation. Watches for changes via <see cref="IConfiguration.GetReloadToken"/>.
/// </summary>
public sealed class MicrosoftConfigurationProvider(
    MicrosoftConfigurationProviderOptions options
) : ConfigurationProvider<MicrosoftConfigurationProviderOptions, MicrosoftConfigurationProviderQueryOptions>(options)
{
    public override Task<byte[]> FetchConfigurationBytesAsync(
        MicrosoftConfigurationProviderQueryOptions query,
        CancellationToken ct = default)
    {
        var dict = Flatten(ProviderOptions.Configuration);
        var bytes = DictToJsonBytes(dict);
        return Task.FromResult(bytes);
    }

    public override IObservable<byte[]> ChangesAsBytes(MicrosoftConfigurationProviderQueryOptions query)
    {
        return new ChangeTokenObservable(this, query);
    }

    private static Dictionary<string, string?> Flatten(IConfiguration configuration)
    {
        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in configuration.AsEnumerable(makePathsRelative: true))
        {
            if (kv.Value is null || string.IsNullOrWhiteSpace(kv.Key))
            {
                continue;
            }

            dict[kv.Key] = kv.Value;
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
    /// Wraps <see cref="IConfiguration.GetReloadToken"/> as an <see cref="IObservable{T}"/>.
    /// Re-registers after each callback since <see cref="IChangeToken"/> is single-fire.
    /// </summary>
    private sealed class ChangeTokenObservable(
        MicrosoftConfigurationProvider owner,
        MicrosoftConfigurationProviderQueryOptions query) : IObservable<byte[]>
    {
        public IDisposable Subscribe(IObserver<byte[]> observer)
        {
            var state = new ChangeTokenState(owner, query, observer);
            state.Register();
            return state;
        }

        private sealed class ChangeTokenState : IDisposable
        {
            private readonly MicrosoftConfigurationProvider _owner;
            private readonly MicrosoftConfigurationProviderQueryOptions _query;
            private readonly IObserver<byte[]> _observer;
            private IDisposable? _registration;
            private int _disposed;

            public ChangeTokenState(
                MicrosoftConfigurationProvider owner,
                MicrosoftConfigurationProviderQueryOptions query,
                IObserver<byte[]> observer)
            {
                _owner = owner;
                _query = query;
                _observer = observer;
            }

            public void Register()
            {
                if (Volatile.Read(ref _disposed) != 0) return;

                // Watch the root configuration for changes (not the section),
                // because IConfigurationSection.GetReloadToken() delegates to the root anyway.
                var token = _owner.ProviderOptions.Configuration.GetReloadToken();
                _registration = token.RegisterChangeCallback(_ => OnChange(), null);
            }

            private void OnChange()
            {
                if (Volatile.Read(ref _disposed) != 0) return;

                var dict = Flatten(_owner.ProviderOptions.Configuration);
                var bytes = DictToJsonBytes(dict);
                try { _observer.OnNext(bytes); }
                catch { /* observer fault must not break re-registration */ }

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
