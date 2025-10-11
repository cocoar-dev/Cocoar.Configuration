using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Helper;
using Cocoar.Configuration.Infrastructure;
using Cocoar.Configuration.Providers.Abstractions;
using Cocoar.Configuration.Utilities;
using Microsoft.Extensions.Logging;

namespace Cocoar.Configuration.Rules;

internal sealed class RuleManager(ConfigRule rule, ILogger logger, ProviderRegistry registry) : IDisposable
{
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

    public Type TypeDefinition => rule.ConcreteType;
    public bool Required => rule.Options?.Required == true;
    public IObservable<bool> Changes => _changes.AsObservable();

    internal Dictionary<string, JsonElement>? LastFlatContribution { get; set; }
    internal string? LastSelectionHash { get; set; }

    public async Task<(bool include, JsonElement value)> ComputeAsync(IConfigurationAccessor accessor, CancellationToken ct)
    {
        LastFailureException = null;

        if (ShouldSkipViaUseWhen())
        {
            return SkipResult();
        }

        var providerOptions = rule.ResolveProviderOptions(accessor);
        EnsureProvider(providerOptions);

        var queryOptions = rule.ResolveQueryOptions(accessor);
        EnsureSubscription(queryOptions);

        try
        {
            var value = await _provider!.FetchConfigurationAsync(queryOptions, ct).ConfigureAwait(false);

            if (!TryApplySelectAndMount(value, out var transformed))
            {
                return SkipResult();
            }
            value = transformed;
            
            LastOutcome = RuleExecutionOutcome.Up;
            return (include: true, value);
        }
        catch (Exception ex)
        {
            return HandleFailure(ex);
        }
    }

    private bool ShouldSkipViaUseWhen()
    {
        if (rule.Options?.UseWhen == null)
        {
            return false;
        }

        if (rule.Options.UseWhen.Invoke())
        {
            return false;
        }

        Unsubscribe();
        LastOutcome = RuleExecutionOutcome.Skipped;
        return true;
    }

    private void EnsureProvider(IProviderConfiguration providerOptions)
    {
        var newProviderKey = providerOptions.GenerateProviderKey();
        if (_provider != null && _providerKey == newProviderKey)
        {
            return;
        }

        RebuildProvider(providerOptions, newProviderKey);
    }

    private void EnsureSubscription(IProviderQuery queryOptions)
    {
        var newQueryKey = ComputeQueryKey(queryOptions);
        if (_subscription != null && _queryKey == newQueryKey)
        {
            return;
        }

        Resubscribe(queryOptions, newQueryKey);
    }

    private bool TryApplySelectAndMount(JsonElement value, out JsonElement transformed)
    {
        transformed = value;

        var selectPath = rule.Options?.SelectPath;
        if (!string.IsNullOrWhiteSpace(selectPath))
        {
            try
            {
                transformed = JsonHelper.SelectColonDelimited(transformed, selectPath);
            }
            catch (Exception ex)
            {
                return HandleSelectFailure(selectPath, ex, out transformed);
            }
        }

        var mountPath = rule.Options?.MountPath;
        if (!string.IsNullOrWhiteSpace(mountPath))
        {
            transformed = JsonHelper.WrapIfNeeded(transformed, mountPath);
        }

        return true;
    }

    private void RebuildProvider(IProviderConfiguration providerOptions, string? providerKey)
    {
        Unsubscribe();
        if (_providerHandle is not null)
        {
            Safety.DisposeQuietly(_providerHandle);
            _providerHandle = null;
        }

        _providerHandle = registry.Acquire(rule.ProviderType, providerOptions);
        _provider = _providerHandle.Provider;
        _providerKey = providerKey;
        LastSelectionHash = null;
    }

    private void Resubscribe(IProviderQuery queryOptions, string queryKey)
    {
        Unsubscribe();
        _queryKey = queryKey;
        LastSelectionHash = null;

        if (_provider is null)
        {
            return;
        }

        _subscription = _provider
            .Changes(queryOptions)
            .Subscribe(
                ProcessProviderChange,
                _ => PublishChangeSafely());
    }

    private void ProcessProviderChange(JsonElement element)
    {
        try
        {
            var selected = ApplySelect(element);
            var hash = ComputeSelectionHash(selected);
            if (hash == LastSelectionHash)
            {
                return;
            }

            LastSelectionHash = hash;
            PublishChangeSafely();
        }
        catch
        {
            // ignore
        }
    }

    private void PublishChangeSafely()
    {
        Safety.NotifyQuietly(_changes, true);
    }

    private bool HandleSelectFailure(string selectPath, Exception ex, out JsonElement transformed)
    {
        transformed = default;

        if (Required)
        {
            throw new InvalidOperationException($"Selection path '{selectPath}' failed for provider {rule.ProviderType.Name}", ex);
        }

        logger.LogWarning(ex, "Selection path '{SelectPath}' failed; skipping optional rule.", selectPath);
        LastOutcome = RuleExecutionOutcome.Skipped;
        return false;
    }

    private (bool include, JsonElement value) HandleFailure(Exception ex)
    {
        LastOutcome = RuleExecutionOutcome.Failed;
        LastFailureException = ex;

        if (Required)
        {
            logger.LogError(ex, "Required rule failed: {Provider}->{Config}", rule.ProviderType.Name, rule.ConcreteType.Name);
            throw new InvalidOperationException($"Required rule failed for {rule.ProviderType.Name} → {rule.ConcreteType.Name}", ex);
        }

        logger.LogWarning(ex, "Optional rule failed and will be skipped: {Provider}->{Config}", rule.ProviderType.Name, rule.ConcreteType.Name);
        return SkipResult();
    }

    private static (bool include, JsonElement value) SkipResult() => (include: false, value: default);


    private JsonElement ApplySelect(JsonElement value)
    {
        var selectPath = rule.Options?.SelectPath;
        if (!string.IsNullOrWhiteSpace(selectPath))
        {
            try { value = JsonHelper.SelectColonDelimited(value, selectPath); }
            catch { /* ignore */ }
        }
        return value;
    }

    private static string ComputeSelectionHash(JsonElement value)
    {
        try
        {
            using var md5 = System.Security.Cryptography.MD5.Create();
            using var stream = new System.Security.Cryptography.CryptoStream(Stream.Null, md5, System.Security.Cryptography.CryptoStreamMode.Write);
            using var writer = new Utf8JsonWriter(stream);

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
            Safety.DisposeQuietly(_subscription);
            _subscription = null;
        }
    }

    private static string ComputeQueryKey(IProviderQuery query)
    {
        try
        {
            using var md5 = System.Security.Cryptography.MD5.Create();
            using var stream = new System.Security.Cryptography.CryptoStream(Stream.Null, md5, System.Security.Cryptography.CryptoStreamMode.Write);
            using var writer = new Utf8JsonWriter(stream);

            JsonSerializer.Serialize(writer, query, query.GetType());
            writer.Flush();
            stream.FlushFinalBlock();

            return Convert.ToHexString(md5.Hash!);
        }
        catch
        {
            return JsonSerializer.Serialize(query, query.GetType());
        }
    }

    public void Dispose()
    {
        Unsubscribe();
        if (_providerHandle is not null)
        {
            Safety.DisposeQuietly(_providerHandle);
            _providerHandle = null;
        }
        _provider = null;
        _changes.OnCompleted();
        _changes.Dispose();
    }

}
