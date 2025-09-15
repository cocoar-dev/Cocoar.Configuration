using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json;
using Cocoar.Configuration.Providers.Abstractions;
using Microsoft.Extensions.Logging;

namespace Cocoar.Configuration;

internal sealed class RuleManager : IDisposable
{
    private readonly ConfigRule _rule;
    private readonly ILogger _logger;
    private readonly ProviderRegistry _registry;
    private ProviderRegistry.ProviderHandle? _providerHandle;
    private ConfigurationProvider? _provider;
    private string? _providerKey;
    private IDisposable? _subscription;
    private string? _queryKey;
    private readonly Subject<bool> _changes = new();

    public RuleManager(ConfigRule rule, ILogger logger, ProviderRegistry registry)
    {
        _rule = rule;
        _logger = logger;
        _registry = registry;
    }

    public ConfigRegistration TypeDefinition => _rule.Registration;
    public bool Required => _rule.Options?.Required == true;
    public IObservable<bool> Changes => _changes.AsObservable();

    // Per-rule flattened contribution from last successful compute (already flattened, not merged)
    internal Dictionary<string, JsonElement>? LastFlatContribution { get; set; }
    internal string? LastSelectionHash { get; set; }

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
        var newProviderKey = providerOptions.GenerateProviderKey();
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
            var value = await _provider!.FetchConfigurationAsync(queryOptions, ct).ConfigureAwait(false);

            // Select stage
            var selectPath = _rule.Options?.SelectPath;
            if (!string.IsNullOrWhiteSpace(selectPath))
            {
                try
                {
                    value = Json.JsonPath.SelectColonDelimited(value, selectPath);
                }
                catch (Exception ex)
                {
                    if (Required)
                        throw new InvalidOperationException($"Selection path '{selectPath}' failed for provider {_rule.ProviderType.Name}", ex);
                    _logger.LogWarning(ex, "Selection path '{SelectPath}' failed; skipping optional rule.", selectPath);
                    return (include: false, value: default);
                }
            }

            // Mount stage
            var mountPath = _rule.Options?.MountPath;
            if (!string.IsNullOrWhiteSpace(mountPath))
            {
                value = Json.JsonPath.WrapIfNeeded(value, mountPath);
            }
            return (include: true, value);
        }
        catch (Exception ex)
        {
            if (Required)
            {
                _logger.LogError(ex, "Required rule failed: {Provider}->{Config}", _rule.ProviderType.Name, _rule.Registration.ConcreteType.Name);
                throw new InvalidOperationException($"Required rule failed for {_rule.ProviderType.Name} → {_rule.Registration.ConcreteType.Name}", ex);
            }
            _logger.LogWarning(ex, "Optional rule failed and will be skipped: {Provider}->{Config}", _rule.ProviderType.Name, _rule.Registration.ConcreteType.Name);
            return (include: false, value: default);
        }
    }

    private void RebuildProvider(IProviderConfiguration providerOptions)
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
        _providerKey = providerOptions.GenerateProviderKey();
    }

    private void Resubscribe(IProviderQuery queryOptions)
    {
        Unsubscribe();
        _queryKey = ComputeQueryKey(queryOptions);
        _subscription = _provider!
            .Changes(queryOptions)
            .Subscribe(
                element =>
                {
                    try
                    {
                        var selected = ApplySelect(element);
                        var hash = ComputeSelectionHash(selected);
                        if (hash == LastSelectionHash) return; // suppress identical selection
                        LastSelectionHash = hash;
                        _changes.OnNext(true);
                    }
                    catch { /* suppress selection errors; provider fetch path will surface them */ }
                },
                _ =>
                {
                    try { _changes.OnNext(true); } catch { /* ignore */ }
                }
            );
    }

    private JsonElement ApplySelect(JsonElement value)
    {
        var selectPath = _rule.Options?.SelectPath;
        if (!string.IsNullOrWhiteSpace(selectPath))
        {
            try { value = Json.JsonPath.SelectColonDelimited(value, selectPath); }
            catch { /* ignore here; gating only */ }
        }
        return value;
    }

    private static string ComputeSelectionHash(JsonElement value)
    {
        try
        {
            var raw = value.GetRawText();
            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(raw);
            return Convert.ToHexString(sha.ComputeHash(bytes));
        }
        catch { return string.Empty; }
    }

    private void Unsubscribe()
    {
        if (_subscription != null)
        {
            try { _subscription.Dispose(); } catch { /* ignore */ }
            _subscription = null;
        }
    }

    private static string ComputeQueryKey(IProviderQuery query)
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
