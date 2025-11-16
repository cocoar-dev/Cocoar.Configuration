using Microsoft.Extensions.Logging;
using System.Text.Json;
using Cocoar.Configuration.Infrastructure;
using Cocoar.Configuration.Rules;
using Cocoar.Configuration.Reactive;
using Cocoar.Configuration.Utilities;
using Cocoar.Json.Mutable;

namespace Cocoar.Configuration.Core;

/// <summary>
/// Central engine for configuration computation and change management.
/// Handles initialization, recomputation, task scheduling, and change subscriptions.
/// </summary>
internal class ConfigurationEngine : IDisposable
{
    private readonly ConfigurationState _state;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _recomputeSemaphore = new(1, 1);
    private readonly Lock _recomputeGate = new();
    
    private CancellationTokenSource? _recomputeCts;
    private Task? _currentRecomputeTask;
    private bool _disposed;
    private readonly List<IDisposable> _changeSubscriptions = new();

    public ConfigurationEngine(ConfigurationState state, ILogger logger)
    {
        _state = state;
        _logger = logger;
    }

    public Task? CurrentRecomputeTask => _currentRecomputeTask;

    /// <summary>
    /// Initializes the configuration system: analyzes rules, creates managers, performs initial computation, and sets up subscriptions.
    /// </summary>
    public void InitializeAndCompute(
        List<ConfigRule> rules,
        List<RuleManager> ruleManagers,
        ProviderRegistry providerRegistry,
        IConfigurationAccessor configAccessor,
        Action<int> scheduleRecomputeCallback,
        int debounceMilliseconds)
    {
        ruleManagers.Clear();
        ruleManagers.AddRange(rules.Select(rule => new RuleManager(rule, _logger, providerRegistry)));

        try
        {
            RecomputeAllConfigurationsSafe(ruleManagers, configAccessor);
            CreateChangeSubscriptions(ruleManagers, scheduleRecomputeCallback, debounceMilliseconds);
            
            _state.ReportSuccessfulRecompute(0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ConfigManager initialization failed");
            _state.ReportFailedRecompute(0, ex);
            throw;
        }
    }

    /// <summary>
    /// Schedules a configuration recomputation starting from the given index.
    /// Cancels any in-flight recompute and starts a new one.
    /// </summary>
    public void ScheduleRecompute(
        List<RuleManager> ruleManagers,
        IConfigurationAccessor configAccessor,
        ReactiveConfigManager reactiveConfigManager,
        int startIndex)
    {
        lock (_recomputeGate)
        {
            var cts = RenewCancellationSource();

            _currentRecomputeTask = Task.Run(() =>
            {
                try
                {
                    RecomputeAllConfigurationsSafe(ruleManagers, configAccessor, startIndex, cts.Token);
                    _state.ReportSuccessfulRecompute(startIndex);
                    reactiveConfigManager.NotifyConfigurationObservers(configAccessor.GetConfig);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Runtime recompute failed - preserving current configuration");
                    _state.ReportFailedRecompute(startIndex, ex);
                }
            }, cts.Token);
        }
    }

    /// <summary>
    /// Recomputes all configurations starting from the given index, with semaphore protection and error handling.
    /// </summary>
    public void RecomputeAllConfigurationsSafe(
        IEnumerable<RuleManager> ruleManagers,
        IConfigurationAccessor configAccessor,
        int startIndex = 0,
        CancellationToken cancellationToken = default)
    {
        _recomputeSemaphore.Wait(cancellationToken);
        try
        {
            _logger.LogDebug("Recompute started");
            try
            {
                RecomputeAllConfigurations(ruleManagers, configAccessor, startIndex, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Recompute cancelled");
                _state.RollbackUpdate();
                throw;
            }
            catch
            {
                _state.RollbackUpdate();
                throw;
            }
            finally
            {
                _logger.LogDebug("Recompute finished");
            }
        }
        finally
        {
            _recomputeSemaphore.Release();
        }
    }

    /// <summary>
    /// Async version of RecomputeAllConfigurationsSafe.
    /// </summary>
    public async Task RecomputeAllConfigurationsSafeAsync(
        IEnumerable<RuleManager> ruleManagers,
        IConfigurationAccessor configAccessor,
        int startIndex = 0,
        CancellationToken cancellationToken = default)
    {
        await _recomputeSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _logger.LogDebug("Recompute started");
            try
            {
                await RecomputeAllConfigurationsAsync(ruleManagers, configAccessor, startIndex, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Recompute cancelled");
                _state.RollbackUpdate();
                throw;
            }
            catch
            {
                _state.RollbackUpdate();
                throw;
            }
            finally
            {
                _logger.LogDebug("Recompute finished");
            }
        }
        finally
        {
            _recomputeSemaphore.Release();
        }
    }
    private void RecomputeAllConfigurations(
        IEnumerable<RuleManager> ruleManagers,
        IConfigurationAccessor configAccessor,
        int startIndex = 0,
        CancellationToken cancellationToken = default)
    {
        var mergedConfigs = new Dictionary<Type, MutableJsonObject>();
        _state.BeginUpdate();
        var orderedManagers = ruleManagers.ToList();

        cancellationToken.ThrowIfCancellationRequested();

        RestorePrefixContributions(orderedManagers, startIndex, mergedConfigs, cancellationToken);
        RecomputeSuffix(orderedManagers, startIndex, configAccessor, mergedConfigs, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        _state.CommitUpdate(mergedConfigs);
    }

    private async Task RecomputeAllConfigurationsAsync(
        IEnumerable<RuleManager> ruleManagers,
        IConfigurationAccessor configAccessor,
        int startIndex = 0,
        CancellationToken cancellationToken = default)
    {
        var mergedConfigs = new Dictionary<Type, MutableJsonObject>();
        _state.BeginUpdate();
        var orderedManagers = ruleManagers.ToList();

        cancellationToken.ThrowIfCancellationRequested();

        RestorePrefixContributions(orderedManagers, startIndex, mergedConfigs, cancellationToken);
        await RecomputeSuffixAsync(orderedManagers, startIndex, configAccessor, mergedConfigs, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        _state.CommitUpdate(mergedConfigs);
    }

    private void RestorePrefixContributions(
        IReadOnlyList<RuleManager> orderedManagers,
        int startIndex,
        Dictionary<Type, MutableJsonObject> mergedConfigs,
        CancellationToken cancellationToken)
    {
        if (startIndex <= 0)
        {
            return;
        }

        for (var i = 0; i < startIndex && i < orderedManagers.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ruleManager = orderedManagers[i];
            if (ruleManager.LastJsonContribution is not { } lastContribution)
            {
                continue;
            }

            var mergedConfig = GetOrCreateMergedConfig(mergedConfigs, ruleManager.TypeDefinition);
            MutableJsonMerge.Merge(mergedConfig, lastContribution);

            _state.UpdateConfiguration(ruleManager.TypeDefinition, mergedConfig);
        }
    }

    private void RecomputeSuffix(
        IReadOnlyList<RuleManager> orderedManagers,
        int startIndex,
        IConfigurationAccessor configAccessor,
        Dictionary<Type, MutableJsonObject> mergedConfigs,
        CancellationToken cancellationToken)
    {
        for (var i = startIndex; i < orderedManagers.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ruleManager = orderedManagers[i];
            var (include, bytes) = ruleManager.ComputeAsync(configAccessor, cancellationToken).GetAwaiter().GetResult();
            
            ProcessRuleResult(ruleManager, include, bytes, mergedConfigs);
        }
    }

    private async Task RecomputeSuffixAsync(
        IReadOnlyList<RuleManager> orderedManagers,
        int startIndex,
        IConfigurationAccessor configAccessor,
        Dictionary<Type, MutableJsonObject> mergedConfigs,
        CancellationToken cancellationToken)
    {
        for (var i = startIndex; i < orderedManagers.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ruleManager = orderedManagers[i];
            var (include, bytes) = await ruleManager.ComputeAsync(configAccessor, cancellationToken).ConfigureAwait(false);
            
            ProcessRuleResult(ruleManager, include, bytes, mergedConfigs);
        }
    }

    private void ProcessRuleResult(
        RuleManager ruleManager,
        bool include,
        ReadOnlyMemory<byte> bytes,
        Dictionary<Type, MutableJsonObject> mergedConfigs)
    {
        if (!include)
        {
            ruleManager.LastJsonContribution = null;
            return;
        }

        MutableJsonObject newContribution;
        try
        {
            var node = MutableJsonDocument.Parse(bytes.Span);
            if (node is not MutableJsonObject obj)
            {
                throw new JsonException($"Expected JSON object for configuration type {ruleManager.TypeDefinition.Name}, got {node.Kind}");
            }
            
            newContribution = obj;
        }
        catch (Exception ex) when (ex is JsonException || ex is FormatException)
        {
            if (ruleManager.Required)
            {
                throw new InvalidOperationException($"Required rule failed during parse for {ruleManager.TypeDefinition.Name}", ex);
            }
            ruleManager.LastJsonContribution = null;
            return;
        }

        var mergedConfig = GetOrCreateMergedConfig(mergedConfigs, ruleManager.TypeDefinition);
        
        // Lock on mergedConfig to prevent readers from serializing while we're merging
        lock (mergedConfig)
        {
            MutableJsonMerge.Merge(mergedConfig, newContribution);
        }

        ruleManager.LastJsonContribution = newContribution;
        _state.UpdateConfiguration(ruleManager.TypeDefinition, mergedConfig);
    }

    private static MutableJsonObject GetOrCreateMergedConfig(
        Dictionary<Type, MutableJsonObject> mergedConfigs,
        Type type)
    {
        if (!mergedConfigs.TryGetValue(type, out var config))
        {
            config = new MutableJsonObject();
            mergedConfigs[type] = config;
        }

        return config;
    }

    private void CreateChangeSubscriptions(
        IEnumerable<RuleManager> ruleManagers,
        Action<int> recomputeFromIndexCallback,
        int debounceMilliseconds)
    {
        DisposeAllSubscriptions();
        
        var list = ruleManagers.ToList();
        var coalescer = new RecomputeCoalescer(_logger, recomputeFromIndexCallback, debounceMilliseconds, 40);
        _changeSubscriptions.Add(coalescer);

        for (var i = 0; i < list.Count; i++)
        {
            var idx = i;
            var rm = list[i];
            var subscription = rm.Changes.Subscribe(_ =>
            {
                try 
                { 
                    coalescer.Signal(idx); 
                }
                catch (Exception ex) 
                { 
                    _logger.LogError(ex, "Recompute failed from change trigger"); 
                }
            });
            _changeSubscriptions.Add(subscription);
        }
    }

    private void DisposeAllSubscriptions()
    {
        foreach (var subscription in _changeSubscriptions.ToArray())
        {
            Safety.DisposeQuietly(subscription);
        }

        _changeSubscriptions.Clear();
    }

    private CancellationTokenSource RenewCancellationSource()
    {
        var newCts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _recomputeCts, newCts);
        Safety.CancelAndDisposeQuietly(previous);
        return newCts;
    }

    private void DisposeCancellationSource()
    {
        var cts = Interlocked.Exchange(ref _recomputeCts, null);
        Safety.CancelAndDisposeQuietly(cts);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        DisposeCancellationSource();
        DisposeAllSubscriptions();
        _recomputeSemaphore?.Dispose();
        GC.SuppressFinalize(this);
    }
}
