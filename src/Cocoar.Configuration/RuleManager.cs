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

    internal enum RuleExecutionOutcome
    {
        Unknown = 0,
        Up = 1,
        Skipped = 2,
        Failed = 3
    }

    internal RuleExecutionOutcome LastOutcome { get; private set; } = RuleExecutionOutcome.Unknown;
    internal Exception? LastFailureException { get; private set; }

    public RuleManager(ConfigRule rule, ILogger logger, ProviderRegistry registry)
    {
        _rule = rule;
        _logger = logger;
        _registry = registry;
    }

    public Type TypeDefinition => _rule.ConcreteType;
    public bool Required => _rule.Options?.Required == true;
    public IObservable<bool> Changes => _changes.AsObservable();

    // Per-rule flattened contribution from last successful compute (already flattened, not merged)
    internal Dictionary<string, JsonElement>? LastFlatContribution { get; set; }
    internal string? LastSelectionHash { get; set; }

    public async Task<(bool include, JsonElement value)> ComputeAsync(ConfigManager accessor, CancellationToken ct)
    {
        LastFailureException = null; // reset per attempt
        // Handle UseWhen predicate
        if (_rule.Options?.UseWhen != null && !_rule.Options.UseWhen.Invoke())
        {
            // Ensure unsubscribed if previously active
            Unsubscribe();
            LastOutcome = RuleExecutionOutcome.Skipped;
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
                    LastOutcome = RuleExecutionOutcome.Skipped;
                    return (include: false, value: default);
                }
            }

            // Mount stage
            var mountPath = _rule.Options?.MountPath;
            if (!string.IsNullOrWhiteSpace(mountPath))
            {
                value = Json.JsonPath.WrapIfNeeded(value, mountPath);
            }
            LastOutcome = RuleExecutionOutcome.Up;
            return (include: true, value);
        }
        catch (Exception ex)
        {
            if (Required)
            {
                _logger.LogError(ex, "Required rule failed: {Provider}->{Config}", _rule.ProviderType.Name, _rule.ConcreteType.Name);
                LastOutcome = RuleExecutionOutcome.Failed;
                LastFailureException = ex;
                throw new InvalidOperationException($"Required rule failed for {_rule.ProviderType.Name} → {_rule.ConcreteType.Name}", ex);
            }
            _logger.LogWarning(ex, "Optional rule failed and will be skipped: {Provider}->{Config}", _rule.ProviderType.Name, _rule.ConcreteType.Name);
            LastOutcome = RuleExecutionOutcome.Failed; // distinguish from ordinary skip
            LastFailureException = ex;
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
        LastSelectionHash = null; // Clear hash gating state on provider rebuild
    }

    private void Resubscribe(IProviderQuery queryOptions)
    {
        Unsubscribe();
        _queryKey = ComputeQueryKey(queryOptions);
        LastSelectionHash = null; // Clear hash gating state on new subscription
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
            using var md5 = System.Security.Cryptography.MD5.Create();
            using var stream = new System.Security.Cryptography.CryptoStream(System.IO.Stream.Null, md5, System.Security.Cryptography.CryptoStreamMode.Write);
            using var writer = new System.Text.Json.Utf8JsonWriter(stream);
            
            value.WriteTo(writer);
            writer.Flush();
            stream.FlushFinalBlock();
            
            return Convert.ToHexString(md5.Hash!);
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
        // Use streaming serialization for stable key generation without string allocation
        try
        {
            using var md5 = System.Security.Cryptography.MD5.Create();
            using var stream = new System.Security.Cryptography.CryptoStream(System.IO.Stream.Null, md5, System.Security.Cryptography.CryptoStreamMode.Write);
            using var writer = new System.Text.Json.Utf8JsonWriter(stream);
            
            System.Text.Json.JsonSerializer.Serialize(writer, query, query.GetType());
            writer.Flush();
            stream.FlushFinalBlock();
            
            return Convert.ToHexString(md5.Hash!);
        }
        catch
        {
            // Fallback to simple serialization for unusual query types
            return System.Text.Json.JsonSerializer.Serialize(query, query.GetType());
        }
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
