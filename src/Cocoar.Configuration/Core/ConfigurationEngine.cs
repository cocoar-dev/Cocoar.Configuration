using Microsoft.Extensions.Logging;
using System.Text.Json;
using Cocoar.Capabilities;
using Cocoar.Configuration.Infrastructure;
using Cocoar.Configuration.Rules;
using Cocoar.Configuration.Utilities;
using Cocoar.Json.Mutable;

namespace Cocoar.Configuration.Core;

internal static partial class ConfigurationEngineLog
{
    [LoggerMessage(EventId = 2000, Level = LogLevel.Error, Message = "ConfigManager initialization failed")]
    public static partial void InitializationFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Error, Message = "Runtime recompute failed - preserving current configuration")]
    public static partial void RuntimeRecomputeFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Debug, Message = "Recompute started")]
    public static partial void RecomputeStarted(this ILogger logger);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Debug, Message = "Recompute cancelled")]
    public static partial void RecomputeCancelled(this ILogger logger);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Debug, Message = "Recompute finished")]
    public static partial void RecomputeFinished(this ILogger logger);

    [LoggerMessage(EventId = 2005, Level = LogLevel.Error, Message = "Recompute failed from change trigger")]
    public static partial void RecomputeFailedFromChange(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2006, Level = LogLevel.Information, Message = "Startup phase complete - switching to resilient mode")]
    public static partial void StartupComplete(this ILogger logger);
}

/// <summary>
/// Central engine for configuration computation and change management.
/// Handles initialization, recomputation, and change subscriptions.
/// Delegates scheduling/cancellation to RecomputeScheduler.
/// </summary>
internal class ConfigurationEngine : IDisposable, IAsyncDisposable
{
    private readonly ConfigurationState _state;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _recomputeSemaphore = new(1, 1);
    private readonly RecomputeScheduler _scheduler = new();

    private bool _disposed;
    private readonly List<IDisposable> _changeSubscriptions = [];

    // Context for deserialization
    private ExposureRegistry? _bindingRegistry;
    private ConfigManagerCapabilityScope? _capabilityScope;

    public ConfigurationEngine(ConfigurationState state, ILogger logger)
    {
        _state = state;
        _logger = logger;
    }

    public Task? CurrentRecomputeTask => _scheduler.CurrentRecomputeTask;

    /// <summary>
    /// Initializes the configuration system: analyzes rules, creates managers, performs initial computation, and sets up subscriptions.
    /// Throws ConfigurationDeserializationException if any deserialization fails during startup (fail-fast behavior).
    /// </summary>
    public void InitializeAndCompute(
        List<ConfigRule> rules,
        List<RuleManager> ruleManagers,
        ProviderRegistry providerRegistry,
        IConfigurationAccessor configAccessor,
        ExposureRegistry bindingRegistry,
        ConfigManagerCapabilityScope capabilityScope,
        Action<int> scheduleRecomputeCallback,
        int debounceMilliseconds)
    {
        // Store context for runtime recomputes
        _bindingRegistry = bindingRegistry;
        _capabilityScope = capabilityScope;

        // Initialize the backplane
        _state.InitializeBackplane(bindingRegistry);

        ruleManagers.Clear();
        ruleManagers.AddRange(rules.Select(rule => new RuleManager(rule, _logger, providerRegistry)));

        try
        {
            // During startup, deserialization failures throw
            RecomputeAllConfigurationsSafe(ruleManagers, configAccessor);
            CreateChangeSubscriptions(ruleManagers, scheduleRecomputeCallback, debounceMilliseconds);

            _state.ReportSuccessfulRecompute(0);

            // Mark startup complete - future failures will preserve last good config
            _state.MarkStartupComplete();
            _logger.StartupComplete();
        }
        catch (Exception ex)
        {
            _logger.InitializationFailed(ex);
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
        int startIndex)
    {
        _scheduler.ScheduleAsync(async ct =>
        {
            try
            {
                await RecomputeAllConfigurationsSafeAsync(ruleManagers, configAccessor, startIndex, ct).ConfigureAwait(false);
                _state.ReportSuccessfulRecompute(startIndex);

                if (_state.LastDeserializationFailures.Count > 0)
                    _state.ReportDeserializationFailures(_state.LastDeserializationFailures);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.RuntimeRecomputeFailed(ex);
                _state.ReportFailedRecompute(startIndex, ex);
            }
        });
    }

    /// <summary>
    /// Async variant of <see cref="InitializeAndCompute"/>. Used by <see cref="ConfigManager.InitializeAsync"/>.
    /// </summary>
    public async Task InitializeAndComputeAsync(
        List<ConfigRule> rules,
        List<RuleManager> ruleManagers,
        ProviderRegistry providerRegistry,
        IConfigurationAccessor configAccessor,
        ExposureRegistry bindingRegistry,
        ConfigManagerCapabilityScope capabilityScope,
        Action<int> scheduleRecomputeCallback,
        int debounceMilliseconds,
        CancellationToken cancellationToken = default)
    {
        _bindingRegistry = bindingRegistry;
        _capabilityScope = capabilityScope;

        _state.InitializeBackplane(bindingRegistry);

        ruleManagers.Clear();
        ruleManagers.AddRange(rules.Select(rule => new RuleManager(rule, _logger, providerRegistry)));

        try
        {
            await RecomputeAllConfigurationsSafeAsync(ruleManagers, configAccessor, 0, cancellationToken).ConfigureAwait(false);
            CreateChangeSubscriptions(ruleManagers, scheduleRecomputeCallback, debounceMilliseconds);

            _state.ReportSuccessfulRecompute(0);
            _state.MarkStartupComplete();
            _logger.StartupComplete();
        }
        catch (Exception ex)
        {
            _logger.InitializationFailed(ex);
            _state.ReportFailedRecompute(0, ex);
            throw;
        }
    }

    /// <summary>
    /// Recomputes all configurations starting from the given index, with semaphore protection and error handling.
    /// </summary>
    public void RecomputeAllConfigurationsSafe(
        IReadOnlyList<RuleManager> ruleManagers,
        IConfigurationAccessor configAccessor,
        int startIndex = 0,
        CancellationToken cancellationToken = default)
    {
        _recomputeSemaphore.Wait(cancellationToken);
        try
        {
            _logger.RecomputeStarted();
            try
            {
                RecomputeAllConfigurations(ruleManagers, configAccessor, startIndex, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _logger.RecomputeCancelled();
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
                _logger.RecomputeFinished();
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
        IReadOnlyList<RuleManager> ruleManagers,
        IConfigurationAccessor configAccessor,
        int startIndex = 0,
        CancellationToken cancellationToken = default)
    {
        await _recomputeSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _logger.RecomputeStarted();
            try
            {
                await RecomputeAllConfigurationsAsync(ruleManagers, configAccessor, startIndex, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.RecomputeCancelled();
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
                _logger.RecomputeFinished();
            }
        }
        finally
        {
            _recomputeSemaphore.Release();
        }
    }

    private void RecomputeAllConfigurations(
        IReadOnlyList<RuleManager> ruleManagers,
        IConfigurationAccessor configAccessor,
        int startIndex = 0,
        CancellationToken cancellationToken = default)
    {
        var mergedConfigs = new Dictionary<Type, MutableJsonObject>();
        _state.BeginUpdate();

        cancellationToken.ThrowIfCancellationRequested();

        RestorePrefixContributions(ruleManagers, startIndex, mergedConfigs, cancellationToken);
        RecomputeSuffix(ruleManagers, startIndex, configAccessor, mergedConfigs, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        // Use new method with eager deserialization
        if (_bindingRegistry != null && _capabilityScope != null)
        {
            _state.CommitUpdateWithDeserialization(mergedConfigs, _bindingRegistry, _capabilityScope);
        }
        else
        {
            // Fallback for tests or edge cases
            _state.CommitUpdate(mergedConfigs);
        }
    }

    private async Task RecomputeAllConfigurationsAsync(
        IReadOnlyList<RuleManager> ruleManagers,
        IConfigurationAccessor configAccessor,
        int startIndex = 0,
        CancellationToken cancellationToken = default)
    {
        var mergedConfigs = new Dictionary<Type, MutableJsonObject>();
        _state.BeginUpdate();

        cancellationToken.ThrowIfCancellationRequested();

        RestorePrefixContributions(ruleManagers, startIndex, mergedConfigs, cancellationToken);
        await RecomputeSuffixAsync(ruleManagers, startIndex, configAccessor, mergedConfigs, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        // Use new method with eager deserialization
        if (_bindingRegistry != null && _capabilityScope != null)
        {
            _state.CommitUpdateWithDeserialization(mergedConfigs, _bindingRegistry, _capabilityScope);
        }
        else
        {
            // Fallback for tests or edge cases
            _state.CommitUpdate(mergedConfigs);
        }
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
            var bytes = ruleManager.ComputeAsync(configAccessor, cancellationToken).GetAwaiter().GetResult();

            ProcessRuleResult(ruleManager, bytes, mergedConfigs);
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
            var bytes = await ruleManager.ComputeAsync(configAccessor, cancellationToken).ConfigureAwait(false);

            ProcessRuleResult(ruleManager, bytes, mergedConfigs);
        }
    }

    private void ProcessRuleResult(
        RuleManager ruleManager,
        ReadOnlyMemory<byte>? bytes,
        Dictionary<Type, MutableJsonObject> mergedConfigs)
    {
        if (!bytes.HasValue)
        {
            ruleManager.LastJsonContribution = null;
            return;
        }

        MutableJsonObject newContribution;
        try
        {
            var node = MutableJsonDocument.Parse(bytes.Value.Span);
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
        IReadOnlyList<RuleManager> ruleManagers,
        Action<int> recomputeFromIndexCallback,
        int debounceMilliseconds)
    {
        DisposeAllSubscriptions();

        var coalescer = new RecomputeCoalescer(_logger, recomputeFromIndexCallback, debounceMilliseconds, 40);
        _changeSubscriptions.Add(coalescer);

        for (var i = 0; i < ruleManagers.Count; i++)
        {
            var idx = i;
            var rm = ruleManagers[i];
            var subscription = rm.Changes.Subscribe(_ =>
            {
                try
                {
                    coalescer.Signal(idx);
                }
                catch (Exception ex)
                {
                    _logger.RecomputeFailedFromChange(ex);
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _scheduler.Dispose();
        DisposeAllSubscriptions();
        _recomputeSemaphore.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await _scheduler.DisposeAsync().ConfigureAwait(false);
        DisposeAllSubscriptions();
        _recomputeSemaphore.Dispose();
    }
}
