using Microsoft.Extensions.Logging;
using System.Text.Json;
using Cocoar.Configuration.Helper;
using Cocoar.Configuration.Rules;

namespace Cocoar.Configuration.Core;


internal class ConfigurationOrchestrator(ConfigurationRepository repository, ILogger logger)
{
    private readonly Lock _recomputeLock = new();


    public async Task RecomputeAllConfigurationsAsync(
        IEnumerable<RuleManager> ruleManagers,
        IConfigurationAccessor configAccessor,
        int startIndex = 0,
        CancellationToken cancellationToken = default)
    {
        var flatMaps = new Dictionary<Type, Dictionary<string, JsonElement>>();
        var keyProvenance = new Dictionary<Type, Dictionary<string, int>>();

        repository.BeginUpdate();
        var orderedManagers = ruleManagers.ToList();

        cancellationToken.ThrowIfCancellationRequested();

        RestorePrefixContributions(
            orderedManagers,
            startIndex,
            flatMaps,
            keyProvenance,
            cancellationToken);

        await RecomputeSuffixAsync(
            orderedManagers,
            startIndex,
            configAccessor,
            flatMaps,
            keyProvenance,
            cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        repository.CommitUpdate(BuildNextConfigSnapshot(flatMaps));
    }

    public void RecomputeAllConfigurationsSafe(
        IEnumerable<RuleManager> ruleManagers,
        IConfigurationAccessor configAccessor,
        int startIndex = 0,
        CancellationToken cancellationToken = default)
    {
        lock (_recomputeLock)
        {
            logger.LogDebug("Recompute started");
            try
            {
                RecomputeAllConfigurationsAsync(ruleManagers, configAccessor, startIndex, cancellationToken).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                logger.LogDebug("Recompute cancelled");
                repository.RollbackUpdate();
                throw;
            }
            catch
            {
                repository.RollbackUpdate();
                throw;
            }
            finally
            {
                logger.LogDebug("Recompute finished");
            }
        }
    }

    private void RestorePrefixContributions(
        IReadOnlyList<RuleManager> orderedManagers,
        int startIndex,
        Dictionary<Type, Dictionary<string, JsonElement>> flatMaps,
        Dictionary<Type, Dictionary<string, int>> keyProvenance,
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
            if (ruleManager.LastFlatContribution is not { } lastContribution)
            {
                continue;
            }

            var flatMap = GetOrCreateFlatMap(flatMaps, ruleManager.TypeDefinition);
            var provenance = GetOrCreateProvenance(keyProvenance, ruleManager.TypeDefinition);

            foreach (var (key, value) in lastContribution)
            {
                flatMap[key] = value;
                provenance[key] = i;
            }

            repository.UpdateConfiguration(ruleManager.TypeDefinition, JsonHelper.Unflatten(flatMap));
        }
    }

    private async Task RecomputeSuffixAsync(
        IReadOnlyList<RuleManager> orderedManagers,
        int startIndex,
        IConfigurationAccessor configAccessor,
        Dictionary<Type, Dictionary<string, JsonElement>> flatMaps,
        Dictionary<Type, Dictionary<string, int>> keyProvenance,
        CancellationToken cancellationToken)
    {
        for (var i = startIndex; i < orderedManagers.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ruleManager = orderedManagers[i];
            var (include, value) = await ruleManager.ComputeAsync(configAccessor, cancellationToken).ConfigureAwait(false);
            if (!include)
            {
                ruleManager.LastFlatContribution = null;
                continue;
            }

            var flatMap = GetOrCreateFlatMap(flatMaps, ruleManager.TypeDefinition);
            var provenance = GetOrCreateProvenance(keyProvenance, ruleManager.TypeDefinition);
            var newContribution = JsonHelper.Flatten(value);

            RemoveObsoleteKeys(ruleManager, newContribution, flatMap, provenance, i);
            ApplyContribution(newContribution, flatMap, provenance, i);

            ruleManager.LastFlatContribution = newContribution;
            repository.UpdateConfiguration(ruleManager.TypeDefinition, JsonHelper.Unflatten(flatMap));
        }
    }

    private static void RemoveObsoleteKeys(
        RuleManager ruleManager,
        Dictionary<string, JsonElement> newContribution,
        Dictionary<string, JsonElement> flatMap,
        Dictionary<string, int> provenance,
        int ruleIndex)
    {
        if (ruleManager.LastFlatContribution is not { } oldContribution)
        {
            return;
        }

        foreach (var oldKey in oldContribution.Keys)
        {
            if (newContribution.ContainsKey(oldKey))
            {
                continue;
            }

            if (provenance.TryGetValue(oldKey, out var contributingRule) && contributingRule == ruleIndex)
            {
                flatMap.Remove(oldKey);
                provenance.Remove(oldKey);
            }
        }
    }

    private static void ApplyContribution(
        Dictionary<string, JsonElement> contribution,
        Dictionary<string, JsonElement> flatMap,
        Dictionary<string, int> provenance,
        int ruleIndex)
    {
        foreach (var (key, value) in contribution)
        {
            flatMap[key] = value;
            provenance[key] = ruleIndex;
        }
    }

    private static Dictionary<string, JsonElement> GetOrCreateFlatMap(
        Dictionary<Type, Dictionary<string, JsonElement>> flatMaps,
        Type type)
    {
        if (!flatMaps.TryGetValue(type, out var map))
        {
            map = new();
            flatMaps[type] = map;
        }

        return map;
    }

    private static Dictionary<string, int> GetOrCreateProvenance(
        Dictionary<Type, Dictionary<string, int>> provenanceCache,
        Type type)
    {
        if (!provenanceCache.TryGetValue(type, out var provenance))
        {
            provenance = new();
            provenanceCache[type] = provenance;
        }

        return provenance;
    }

    private static Dictionary<Type, JsonElement> BuildNextConfigSnapshot(
        Dictionary<Type, Dictionary<string, JsonElement>> flatMaps)
    {
        var nextConfig = new Dictionary<Type, JsonElement>();
        foreach (var (type, flatMap) in flatMaps)
        {
            nextConfig[type] = JsonHelper.Unflatten(flatMap);
        }

        return nextConfig;
    }
}
