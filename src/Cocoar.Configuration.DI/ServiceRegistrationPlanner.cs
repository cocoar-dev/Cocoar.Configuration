using Cocoar.Capabilities;
using Cocoar.Configuration.Configure;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.DI.Capabilities;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.DI;

/// <summary>
/// Builds an in-memory service registration plan from ConfigManager setup.
/// Separates interpretation logic from IServiceCollection manipulation.
/// </summary>
internal static class ServiceRegistrationPlanner
{
    /// <summary>
    /// Creates a registration plan from the ConfigManager's rules and setup definitions.
    /// </summary>
    public static Dictionary<Type, ServiceRegistrationInfo> CreatePlan(ConfigManager configManager)
    {
        var serviceRegistrationInfos = new Dictionary<Type, ServiceRegistrationInfo>();

        // 1. Collect all types from rules
        var typesFromRules = new HashSet<Type>();
        foreach (var rule in configManager.Rules)
        {
            typesFromRules.Add(rule.ConcreteType);
        }

        // 2. Process explicit SetupDefinitions for customization
        var configSpecs = configManager.SetupDefinitions;
        if (configSpecs.Count > 0)
        {
            foreach (var spec in configSpecs)
            {
                if (!configManager.CapabilityScope.Compositions.TryGet(spec, out var bag))
                {
                    continue;
                }

                if (bag.TryGetPrimaryAs<ConcreteTypePrimary<SetupDefinition>>(out var typeCapability))
                {
                    ProcessConcreteType(serviceRegistrationInfos, typeCapability!, bag);
                }

                if (bag.TryGetPrimaryAs<ExposedTypePrimary<SetupDefinition>>(out var exposedCapability))
                {
                    ProcessExposedType(serviceRegistrationInfos, exposedCapability!, bag);
                }
            }
        }

        // 3. Auto-register types from rules that don't have explicit setup definitions
        foreach (var type in typesFromRules)
        {
            if (!serviceRegistrationInfos.ContainsKey(type))
            {
                serviceRegistrationInfos[type] = new ServiceRegistrationInfo
                {
                    Type = type,
                    DisableDefault = false
                };
            }
        }

        // Sort by type name for deterministic ordering
        return serviceRegistrationInfos
            .OrderBy(kvp => kvp.Key.FullName)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    private static void ProcessConcreteType(
        Dictionary<Type, ServiceRegistrationInfo> serviceRegistrationInfos,
        ConcreteTypePrimary<SetupDefinition> primaryCapability,
        IComposition bag)
    {
        if (!serviceRegistrationInfos.TryGetValue(primaryCapability.SelectedType, out var serviceRegistrationInfo))
        {
            serviceRegistrationInfo = new ServiceRegistrationInfo
            {
                Type = primaryCapability.SelectedType,
            };
        }

        if (bag.Has<DisableAutoRegistrationCapability<SetupDefinition>>())
        {
            serviceRegistrationInfo.DisableDefault = true;
        }

        var lifetimeCapabilities = bag.GetAll<ServiceLifetimeCapability<SetupDefinition>>();
        var keyedLifetimeMap = ResolveLifetimeSelections(lifetimeCapabilities);
        foreach (var (key, value) in keyedLifetimeMap)
        {
            serviceRegistrationInfo.ServiceLifetimes[key] = value;
        }

        serviceRegistrationInfos[primaryCapability.SelectedType] = serviceRegistrationInfo;

        var exposeAsCapabilities = bag.GetAll<ExposeAsCapability<SetupDefinition>>();
        var exposedInterfaces = exposeAsCapabilities.Select(x => x.ContractType).Distinct().ToList();

        foreach (var it in exposedInterfaces)
        {
            if (!serviceRegistrationInfos.ContainsKey(it))
            {
                serviceRegistrationInfos[it] = new ServiceRegistrationInfo
                {
                    Type = it,
                };
            }
        }
    }

    private static void ProcessExposedType(
        Dictionary<Type, ServiceRegistrationInfo> serviceRegistrationInfos,
        ExposedTypePrimary<SetupDefinition> primaryCapability,
        IComposition bag)
    {
        if (!serviceRegistrationInfos.TryGetValue(primaryCapability.SelectedType, out var serviceRegistrationInfo))
        {
            serviceRegistrationInfo = new ServiceRegistrationInfo
            {
                Type = primaryCapability.SelectedType,
            };
        }

        if (bag.Has<DisableAutoRegistrationCapability<SetupDefinition>>())
        {
            serviceRegistrationInfo.DisableDefault = true;
        }

        var lifetimeCapabilities = bag.GetAll<ServiceLifetimeCapability<SetupDefinition>>();
        var keyedLifetimeMap = ResolveLifetimeSelections(lifetimeCapabilities);
        foreach (var (key, value) in keyedLifetimeMap)
        {
            serviceRegistrationInfo.ServiceLifetimes[key] = value;
        }

        serviceRegistrationInfos[primaryCapability.SelectedType] = serviceRegistrationInfo;
    }

    private static Dictionary<object, ServiceLifetime> ResolveLifetimeSelections(
        IEnumerable<ServiceLifetimeCapability<SetupDefinition>> lifetimeCapabilities)
    {
        var keyed = new Dictionary<object, ServiceLifetime>();
        foreach (var cap in lifetimeCapabilities)
        {
            keyed[cap.Key ?? ""] = cap.Lifetime;
        }
        return keyed;
    }
}
