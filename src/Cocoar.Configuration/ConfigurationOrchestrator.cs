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
        CancellationToken cancellationToken = default)
    {
        // Flat maps by config contract, merged by rule order (last wins)
        var tempFlatMaps = new Dictionary<ConfigRegistration, Dictionary<string, JsonElement>>();
        
        // Install working snapshot for in-progress reads
        _repository.BeginUpdate();

        foreach (var rm in ruleManagers)
        {
            var (include, value) = await rm.ComputeAsync(configManager, cancellationToken).ConfigureAwait(false);
            if (!include) continue;

            if (!tempFlatMaps.TryGetValue(rm.TypeDefinition, out var flatMap))
            {
                flatMap = new Dictionary<string, JsonElement>();
                tempFlatMaps[rm.TypeDefinition] = flatMap;
            }

            var flatOutcome = JsonConfigurationProcessor.Flatten(value);
            foreach (var kvp in flatOutcome)
                flatMap[kvp.Key] = kvp.Value; // last rule wins per key

            // Update working snapshot for this type so subsequent rules can read it
            var partial = JsonConfigurationProcessor.Unflatten(flatMap);
            _repository.UpdateConfiguration(rm.TypeDefinition, partial);
        }

        var nextConfig = new Dictionary<ConfigRegistration, JsonElement>();
        foreach (var (type, flatMap) in tempFlatMaps)
            nextConfig[type] = JsonConfigurationProcessor.Unflatten(flatMap);

        _repository.CommitUpdate(nextConfig);
    }

    /// <summary>
    /// Safely recomputes all configurations with locking to prevent concurrent modifications.
    /// </summary>
    public void RecomputeAllConfigurationsSafe(
        IEnumerable<RuleManager> ruleManagers,
        ConfigManager configManager)
    {
        // Prevent concurrent recomputes and ensure atomic swap
        lock (_recomputeLock)
        {
            _logger.LogDebug("Recompute started");
            RecomputeAllConfigurationsAsync(ruleManagers, configManager).GetAwaiter().GetResult();
            _logger.LogDebug("Recompute finished");
        }
    }
}
