using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json;
using Cocoar.Configuration.Providers;
using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration;

internal sealed class RuleManager : IDisposable
{
    private readonly ConfigRule _rule;
    private readonly IConfigLogger _logger;
    private readonly ProviderRegistry _registry;
    private ProviderRegistry.ProviderHandle? _providerHandle;
    private ConfigSourceProvider? _provider;
    private string? _providerKey;
    private IDisposable? _subscription;
    private string? _queryKey;
    private readonly Subject<bool> _changes = new();

    public RuleManager(ConfigRule rule, IConfigLogger logger, ProviderRegistry registry)
    {
        _rule = rule;
        _logger = logger;
        _registry = registry;
    }

    public ConfigTypeDefinition TypeDefinition => _rule.ConfigContract;
    public bool Required => _rule.Options?.Required == true;
    public IObservable<bool> Changes => _changes.AsObservable();

    public async Task<(bool include, JsonElement value)> ComputeAsync(ConfigManager accessor, CancellationToken ct)
    {
        // Handle UseWhen predicate
        if (_rule.Options?.UseWhen != null && !_rule.Options.UseWhen.Invoke())
        {
            // Ensure unsubscribed if previously active
            Unsubscribe();
            return (include: false, value: default);
        }

        // Resolve options and manage provider reuse
        var providerOptions = _rule.ResolveProviderOptions(accessor);
        var newProviderKey = providerOptions.CalculateKey();
    if (_provider == null || _providerKey != newProviderKey)
        {
            RebuildProvider(providerOptions);
        }

        // Resolve query and (re)subscribe if needed
        var queryOptions = _rule.ResolveQueryOptions(accessor);
        var newQueryKey = ComputeQueryKey(queryOptions);
        if (_subscription == null || _queryKey != newQueryKey)
        {
            Resubscribe(queryOptions);
        }

        try
        {
            var value = await _provider!.GetValueAsync(queryOptions, ct).ConfigureAwait(false);
            return (include: true, value);
        }
        catch (Exception ex)
        {
            if (Required)
            {
                _logger.Error(ex, "Required rule failed: {0}->{1}", _rule.ProviderType.Name, _rule.ConfigContract.ConfigType.Name);
                throw new InvalidOperationException($"Required rule failed for {_rule.ProviderType.Name} → {_rule.ConfigContract.ConfigType.Name}", ex);
            }
            _logger.Warning(ex, "Optional rule failed and will be skipped: {0}->{1}", _rule.ProviderType.Name, _rule.ConfigContract.ConfigType.Name);
            return (include: false, value: default);
        }
    }

    private void RebuildProvider(ISourceProviderInstanceOptions providerOptions)
    {
        // Dispose current subscription and release provider lease
        Unsubscribe();
        if (_providerHandle is not null)
        {
            try { _providerHandle.Dispose(); } catch { /* ignore */ }
            _providerHandle = null;
        }

        _providerHandle = _registry.Acquire(_rule.ProviderType, providerOptions);
        _provider = _providerHandle.Provider;
        _providerKey = providerOptions.CalculateKey();
    }

    private void Resubscribe(ISourceProviderQueryOptions queryOptions)
    {
        Unsubscribe();
        _queryKey = ComputeQueryKey(queryOptions);
        _subscription = _provider!
            .Changes(queryOptions)
            .Subscribe(
                _ => { try { _changes.OnNext(true); } catch { /* ignore */ } },
                _ => { try { _changes.OnNext(true); } catch { /* ignore */ } }
            );
    }

    private void Unsubscribe()
    {
        if (_subscription != null)
        {
            try { _subscription.Dispose(); } catch { /* ignore */ }
            _subscription = null;
        }
    }

    private static string ComputeQueryKey(ISourceProviderQueryOptions query)
    {
        // Use simple serialization for a stable key; assumes query types are simple DTOs
        return JsonSerializer.Serialize(query, query.GetType());
    }

    public void Dispose()
    {
        Unsubscribe();
        if (_providerHandle is not null)
        {
            try { _providerHandle.Dispose(); } catch { /* ignore */ }
            _providerHandle = null;
        }
        _provider = null;
        _changes.OnCompleted();
        _changes.Dispose();
    }
}
