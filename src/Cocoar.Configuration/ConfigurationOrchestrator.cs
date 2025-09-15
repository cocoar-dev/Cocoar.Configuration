using Microsoft.Extensions.Logging;
using System.Text.Json;
using Cocoar.Configuration.Utilities;

namespace Cocoar.Configuration;

/// <summary>
/// Orchestrates the configuration computation process across multiple rule managers.
/// Handles the recomputation logic and merging of configurations from different providers.
/// </summary>
internal class ConfigurationOrchestrator
{
    private readonly ConfigurationRepository _repository;
    private readonly ILogger _logger;
    private readonly Lock _recomputeLock = new();

    public ConfigurationOrchestrator(ConfigurationRepository repository, ILogger logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Recomputes all configurations from the provided rule managers.
    /// </summary>
    public async Task RecomputeAllConfigurationsAsync(
        IEnumerable<RuleManager> ruleManagers,
        ConfigManager configManager,
        int startIndex = 0,
        CancellationToken cancellationToken = default)
    {
    // Flat maps by config contract, merged by rule order (last wins). Seeded by prefix replay logic.
    var tempFlatMaps = new Dictionary<ConfigRegistration, Dictionary<string, JsonElement>>();
    // Track which rule contributed each key to enable safe deletion
    var keyProvenance = new Dictionary<ConfigRegistration, Dictionary<string, int>>();

        _repository.BeginUpdate();
        var list = ruleManagers.ToList();

        cancellationToken.ThrowIfCancellationRequested();

        // For prefix before startIndex, replay stored per-rule flat contributions instead of re-fetching
        if (startIndex > 0)
        {
            for (int i = 0; i < startIndex && i < list.Count; i++)
            {
                var rmPrefix = list[i];
                if (rmPrefix.LastFlatContribution == null) continue; // rule previously skipped/failed

                if (!tempFlatMaps.TryGetValue(rmPrefix.TypeDefinition, out var flatMap))
                {
                    flatMap = new Dictionary<string, JsonElement>();
                    tempFlatMaps[rmPrefix.TypeDefinition] = flatMap;
                }
                if (!keyProvenance.TryGetValue(rmPrefix.TypeDefinition, out var provenance))
                {
                    provenance = new Dictionary<string, int>();
                    keyProvenance[rmPrefix.TypeDefinition] = provenance;
                }
                foreach (var kvp in rmPrefix.LastFlatContribution)
                {
                    flatMap[kvp.Key] = kvp.Value;
                    provenance[kvp.Key] = i; // Track which rule contributed this key
                }

                _repository.UpdateConfiguration(rmPrefix.TypeDefinition, JsonConfigurationProcessor.Unflatten(flatMap));
            }
        }

        for (int i = startIndex; i < list.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rm = list[i];
            var (include, value) = await rm.ComputeAsync(configManager, cancellationToken).ConfigureAwait(false);
            if (!include)
            {
                // Clear previous contribution if rule no longer participates
                rm.LastFlatContribution = null;
                continue;
            }

            if (!tempFlatMaps.TryGetValue(rm.TypeDefinition, out var flatMap))
            {
                flatMap = new Dictionary<string, JsonElement>();
                tempFlatMaps[rm.TypeDefinition] = flatMap;
            }
            if (!keyProvenance.TryGetValue(rm.TypeDefinition, out var provenance))
            {
                provenance = new Dictionary<string, int>();
                keyProvenance[rm.TypeDefinition] = provenance;
            }

            var newFlatContribution = JsonConfigurationProcessor.Flatten(value);
            // Safe deletion handling: only remove keys that were actually contributed by this rule
            if (rm.LastFlatContribution is { } oldContribution)
            {
                foreach (var oldKey in oldContribution.Keys)
                {
                    if (!newFlatContribution.ContainsKey(oldKey))
                    {
                        // Only remove if this rule was the one that contributed the key
                        if (provenance.TryGetValue(oldKey, out var contributingRule) && contributingRule == i)
                        {
                            flatMap.Remove(oldKey);
                            provenance.Remove(oldKey);
                        }
                    }
                }
            }
            foreach (var kvp in newFlatContribution)
            {
                flatMap[kvp.Key] = kvp.Value;
                provenance[kvp.Key] = i; // Track that this rule contributed this key
            }
            rm.LastFlatContribution = newFlatContribution; // store raw per-rule delta

            var unflattened = JsonConfigurationProcessor.Unflatten(flatMap);
            _repository.UpdateConfiguration(rm.TypeDefinition, unflattened);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var nextConfig = new Dictionary<ConfigRegistration, JsonElement>();
        foreach (var (type, flatMap) in tempFlatMaps)
            nextConfig[type] = JsonConfigurationProcessor.Unflatten(flatMap);
        _repository.CommitUpdate(nextConfig);

    // Previous merged flat maps cache removed (no longer required after per-rule contribution + deletion handling).
    }

    /// <summary>
    /// Safely recomputes all configurations with locking to prevent concurrent modifications.
    /// </summary>
    public void RecomputeAllConfigurationsSafe(
        IEnumerable<RuleManager> ruleManagers,
        ConfigManager configManager,
        int startIndex = 0,
        CancellationToken cancellationToken = default)
    {
        lock (_recomputeLock)
        {
            _logger.LogDebug("Recompute started");
            try
            {
                RecomputeAllConfigurationsAsync(ruleManagers, configManager, startIndex, cancellationToken).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Recompute cancelled");
                // Ensure repository not left in pending state
                _repository.RollbackUpdate();
                throw;
            }
            catch
            {
                // On failure ensure pending cleared to avoid inconsistent CurrentConfigurations
                _repository.RollbackUpdate();
                throw;
            }
            finally
            {
                _logger.LogDebug("Recompute finished");
            }
        }
    }
}
