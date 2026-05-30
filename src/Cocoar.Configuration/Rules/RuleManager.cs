using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Diagnostics;
using Cocoar.Configuration.Helper;
using Cocoar.Configuration.Infrastructure;
using Cocoar.Configuration.Providers.Abstractions;
using Cocoar.Json.Mutable;
using Microsoft.Extensions.Logging;

namespace Cocoar.Configuration.Rules;

internal static partial class RuleManagerLog
{
    [LoggerMessage(EventId = 5000, Level = LogLevel.Warning, Message = "Selection path '{SelectPath}' failed; skipping optional rule.")]
    public static partial void OptionalSelectPathFailed(this ILogger logger, Exception exception, string SelectPath);

    [LoggerMessage(EventId = 5001, Level = LogLevel.Error, Message = "Required rule failed: {Provider}->{Config}")]
    public static partial void RequiredRuleFailed(this ILogger logger, Exception exception, string Provider, string Config);

    [LoggerMessage(EventId = 5002, Level = LogLevel.Warning, Message = "Optional rule failed and will be skipped: {Provider}->{Config}")]
    public static partial void OptionalRuleFailed(this ILogger logger, Exception exception, string Provider, string Config);

    [LoggerMessage(EventId = 5003, Level = LogLevel.Debug, Message = "Query key hash failed for {QueryType}; falling back to JSON serialization")]
    public static partial void QueryKeyHashFallback(this ILogger logger, Exception exception, string QueryType);

    [LoggerMessage(EventId = 5004, Level = LogLevel.Debug, Message = "Transform key computation failed; falling back to empty key")]
    public static partial void TransformKeyFallback(this ILogger logger, Exception exception);
}

/// <summary>
/// Coordinates rule execution: provider lifecycle, query management, caching, and change tracking.
/// Delegates lifecycle to RuleProviderLease, caching to TransformCache, and subscriptions to ChangeSubscription.
/// </summary>
internal sealed class RuleManager : IRuleManager
{
    private readonly ConfigRule _rule;
    private readonly ILogger _logger;

    private readonly RuleProviderLease _providerLease;
    private readonly TransformCache _cache = new();
    private readonly ChangeSubscription _changeSubscription = new();

    public RuleExecutionOutcome LastOutcome { get; private set; } = RuleExecutionOutcome.Unknown;
    public Exception? LastFailureException { get; private set; }

    public Type TypeDefinition => _rule.ConcreteType;
    public bool Required => _rule.Options?.Required == true;
    public IObservable<bool> Changes => _changeSubscription.Changes;

    public MutableJsonObject? LastJsonContribution { get; set; }
    public string? LastSelectionHash
    {
        get => _cache.LastSelectionHash;
        set => _cache.LastSelectionHash = value;
    }

    public IReadOnlyList<IRuleManager>? SubManagers => null;

    public ConfigurationProvider? CurrentProvider => _providerLease.Provider;

    public RuleManager(ConfigRule rule, ILogger logger, ProviderRegistry registry)
    {
        _rule = rule;
        _logger = logger;
        _providerLease = new RuleProviderLease(rule.ProviderType, registry);
    }

    public async Task<ReadOnlyMemory<byte>?> ComputeAsync(IConfigurationAccessor accessor, CancellationToken ct)
    {
        LastFailureException = null;

        if (ShouldSkip(accessor))
        {
            return null;  // Skip rule - tenant-scoped without a tenant, or When condition is false
        }

        var providerOptions = _rule.ResolveProviderOptions(accessor);
        EnsureProvider(providerOptions);

        var queryOptions = _rule.ResolveQueryOptions(accessor);
        EnsureSubscription(queryOptions);
        var newTransformKey = ComputeTransformKey(_rule.Options);
        _cache.UpdateTransformKey(newTransformKey);

        try
        {
            if (_cache.HasValidCache)
            {
                LastOutcome = RuleExecutionOutcome.Up;
                return _cache.GetCachedBytes();
            }
            if (_cache.CanReuseWithoutFetch)
            {
                _cache.MarkClean();
                LastOutcome = RuleExecutionOutcome.Up;
                return _cache.GetCachedBytes();
            }
            var bytesMemory = await _providerLease.Provider!.FetchConfigurationBytesAsync(queryOptions, ct).ConfigureAwait(false);

            try
            {
                var transformedBytes = JsonTransform.SelectAndMount(bytesMemory, _rule.Options?.SelectPath, _rule.Options?.MountPath);

                _cache.StoreTransformedBytes(transformedBytes);
            }
            catch (KeyNotFoundException ex)
            {
                if (!HandleSelectFailure(_rule.Options?.SelectPath ?? string.Empty, ex))
                {
                    return EmptyObjectResult();  // Optional rule: return empty object
                }
                throw; // Required path: HandleSelectFailure throws
            }
            finally
            {
                // CRITICAL: Zero provider bytes after use
                if (bytesMemory != null && bytesMemory.Length > 0)
                {
                    CryptographicOperations.ZeroMemory(bytesMemory);
                }
            }

            LastOutcome = RuleExecutionOutcome.Up;
            return _cache.GetCachedBytes();
        }
        catch (Exception ex)
        {
            return HandleFailure(ex);
        }
    }

    private bool ShouldSkip(IConfigurationAccessor accessor)
    {
        // A .TenantScoped() rule never runs in the global (tenant-agnostic) pipeline. Enforced via the static
        // marker (not just the When predicate) so it holds regardless of how .When() and .TenantScoped() were
        // ordered in the fluent chain — e.g. .TenantScoped().When(p) still skips when there is no tenant.
        if (_rule.Options?.TenantScoped == true && string.IsNullOrWhiteSpace(accessor.Tenant))
        {
            return MarkSkipped();
        }

        // A service-backed (Layer-2, ADR-006) rule stays dormant until the application container is built and the
        // activation recompute runs. Enforced via a dedicated gate (not the user .When predicate) so it holds
        // regardless of fluent ordering — a later .When() cannot clobber it (mirrors the .TenantScoped() marker).
        if (_rule.Options?.ActivationGate is { } activationGate && !activationGate.Invoke(accessor))
        {
            return MarkSkipped();
        }

        if (_rule.Options?.UseWhen == null)
        {
            return false;
        }

        if (_rule.Options.UseWhen.Invoke(accessor))
        {
            return false;
        }

        return MarkSkipped();
    }

    private bool MarkSkipped()
    {
        _changeSubscription.Unsubscribe();
        LastOutcome = RuleExecutionOutcome.Skipped;
        return true;
    }

    private void EnsureProvider(IProviderConfiguration providerOptions)
    {
        _providerLease.EnsureProvider(providerOptions, OnBeforeProviderRebuild);
    }

    private void OnBeforeProviderRebuild()
    {
        _changeSubscription.Reset();
        LastSelectionHash = null;
        _cache.Invalidate();
    }

    private void EnsureSubscription(IProviderQuery queryOptions)
    {
        if (!_providerLease.HasProvider)
        {
            return;
        }

        var newQueryKey = ComputeQueryKey(queryOptions);

        bool subscriptionChanged = _changeSubscription.EnsureSubscription(
            _providerLease.Provider!,
            queryOptions,
            newQueryKey,
            ProcessProviderChangeBytes);

        if (subscriptionChanged)
        {
            LastSelectionHash = null;
        }
    }

    private void ProcessProviderChangeBytes(byte[] bytesMemory)
    {
        bool changed = _cache.ProcessProviderChange(bytesMemory, _rule.Options?.SelectPath, _rule.Options?.MountPath);

        if (changed)
        {
            _changeSubscription.PublishChangeSafely();
        }
    }

    private bool HandleSelectFailure(string selectPath, Exception ex)
    {
        if (Required)
        {
            throw new InvalidOperationException($"Selection path '{selectPath}' failed for provider {_rule.ProviderType.Name}", ex);
        }

        _logger.OptionalSelectPathFailed(ex, selectPath);
        LastOutcome = RuleExecutionOutcome.Failed;
        LastFailureException = ex;
        return false;
    }

    private ReadOnlyMemory<byte> HandleFailure(Exception ex)
    {
        LastOutcome = RuleExecutionOutcome.Failed;
        LastFailureException = ex;

        CocoarMetrics.ProviderErrors.Add(1,
            new KeyValuePair<string, object?>("provider_type", _rule.ProviderType.Name),
            new KeyValuePair<string, object?>("required", Required.ToString()));

        if (Required)
        {
            _logger.RequiredRuleFailed(ex, _rule.ProviderType.Name, _rule.ConcreteType.Name);
            throw new InvalidOperationException($"Required rule failed for {_rule.ProviderType.Name} → {_rule.ConcreteType.Name}", ex);
        }

        _logger.OptionalRuleFailed(ex, _rule.ProviderType.Name, _rule.ConcreteType.Name);
        return EmptyObjectResult();
    }

    /// <summary>
    /// Returns empty JSON object - used when optional rules fail but should still contribute empty data.
    /// Health monitoring tracks the failure via LastFailureException.
    /// </summary>
    private static ReadOnlyMemory<byte> EmptyObjectResult()
    {
        return "{}"u8.ToArray();
    }

    private string ComputeQueryKey(IProviderQuery query)
    {
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            var bufferWriter = new ArrayBufferWriter<byte>();
            using var writer = new Utf8JsonWriter(bufferWriter);

            JsonSerializer.Serialize(writer, query, query.GetType());
            writer.Flush();

            var written = bufferWriter.WrittenSpan;
            hash.AppendData(written);

            return Convert.ToHexString(hash.GetHashAndReset());
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            _logger.QueryKeyHashFallback(ex, query.GetType().Name);
            return JsonSerializer.Serialize(query, query.GetType());
        }
    }

    private string ComputeTransformKey(ConfigRuleOptions? options)
    {
        try
        {
            var select = string.IsNullOrWhiteSpace(options?.SelectPath) ? string.Empty : options!.SelectPath!;
            var mount = string.IsNullOrWhiteSpace(options?.MountPath) ? string.Empty : options!.MountPath!;
            var input = select + "|" + mount;
            var bytes = System.Text.Encoding.UTF8.GetBytes(input);
            var hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            _logger.TransformKeyFallback(ex);
            return string.Empty;
        }
    }

    /// <summary>
    /// Clears the cached bytes (zeros them) without disposing the SecureBytes object.
    /// Used to zero plaintext before replacing with encrypted bytes.
    /// </summary>
    public void ClearCachedBytes()
    {
        _cache.ClearCachedBytes();
    }

    /// <summary>
    /// Updates the cached bytes with encrypted/preprocessed bytes.
    /// This prevents plaintext secrets from lingering in memory.
    /// </summary>
    public void UpdateCachedBytes(byte[] encryptedBytes)
    {
        _cache.UpdateCachedBytes(encryptedBytes);
    }

    public void Dispose()
    {
        _changeSubscription.Dispose();
        _cache.Dispose();
        _providerLease.Dispose();
    }
}
