using System.Collections;
using Cocoar.Configuration.Configure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Rules;
using Cocoar.Configuration.Fluent;
using Cocoar.Capabilities;
using Cocoar.Configuration.Reactive;
using Cocoar.Configuration.DI.Capabilities;
using Cocoar.Configuration.Health;

namespace Cocoar.Configuration.DI;


public static class CocoarConfigurationExtensions
{

    public static IServiceCollection AddCocoarConfiguration(
        this IServiceCollection services,
        ConfigManager configManager)
    {
        services.ThrowIfAlreadyRegistered();
        services.AddSingleton(configManager);
        services.AddSingleton<IConfigurationAccessor>(sp => sp.GetRequiredService<ConfigManager>());

        // Register the health service
        services.AddSingleton<IConfigurationHealthService>(sp => sp.GetRequiredService<ConfigManager>().GetHealthService());

        // Collect all types that should be registered
        var typesToRegister = new HashSet<Type>();

        // 1. Auto-register all types from rules (restores pre-SetupBuilder behavior)
        foreach (var rule in configManager.Rules)
        {
            typesToRegister.Add(rule.ConcreteType);
        }

        // 2. Process explicit SetupDefinitions for customization
        var serviceRegistrationInfos = new Dictionary<Type, ServiceRegistrationInfo>();
        var configSpecs = configManager.SetupDefinitions;

        if (configSpecs.Count > 0)
        {
            foreach (var spec in configSpecs)
            {
                // Get the capability bag from the registry using the ConfigureSpec as key
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
        foreach (var type in typesToRegister)
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

        ProcessServiceRegistration(services, serviceRegistrationInfos);

        return services;
    }

    /// <summary>
    /// Adds Cocoar configuration to the service collection using a function-based rule API.
    /// Test configuration overrides are automatically applied via ConfigManager when CocoarTestConfiguration is active.
    /// </summary>
    public static IServiceCollection AddCocoarConfiguration(
        this IServiceCollection services,
        Func<RulesBuilder, ConfigRule[]> rule,
        Func<SetupBuilder, SetupDefinition[]>? setup = null,
        ILogger? logger = null,
        int debounceMilliseconds = 300)
    {
        services.ThrowIfAlreadyRegistered();

        var configManager = new ConfigManager(rule, setup, logger, debounceMilliseconds: debounceMilliseconds);
        configManager.Initialize();

        services.AddCocoarConfiguration(configManager);
        return services;
    }


    private static void ProcessConcreteType(Dictionary<Type, ServiceRegistrationInfo> serviceRegistrationInfos,
        ConcreteTypePrimary<SetupDefinition> primaryCapability, IComposition bag)
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

    private static void ProcessExposedType(Dictionary<Type, ServiceRegistrationInfo> serviceRegistrationInfos,
        ExposedTypePrimary<SetupDefinition> primaryCapability, IComposition bag)
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

    private static void ProcessServiceRegistration(IServiceCollection services, Dictionary<Type, ServiceRegistrationInfo> serviceRegistrationInfos)
    {

        foreach (var (serviceType, value) in serviceRegistrationInfos)
        {
            if (value is { DisableDefault: false, OverwriteDefault: false })
            {
                services.Add(new(serviceType, sp => sp.GetRequiredService<ConfigManager>().GetConfig(serviceType)!, ServiceLifetime.Scoped));
            }

            foreach (var (serviceKey, serviceLifetime) in value.ServiceLifetimes)
            {
                if (serviceKey is "")
                {
                    services.Add(new(serviceType, (sp) => sp.GetRequiredService<ConfigManager>().GetConfig(serviceType)!, serviceLifetime));
                } else
                {
                    services.Add(new(serviceType, serviceKey, (sp, _) => sp.GetRequiredService<ConfigManager>().GetConfig(serviceType)!, serviceLifetime));
                }

            }

            var reactiveType = typeof(IReactiveConfig<>).MakeGenericType(serviceType);
            services.AddSingleton(reactiveType, sp =>
            {
                var mgr = sp.GetRequiredService<ConfigManager>();
                var method = mgr.GetType().GetMethod("GetReactiveConfig")!.MakeGenericMethod(serviceType);
                return method.Invoke(mgr, null)!;
            });
        }

    }


    private static void ThrowIfAlreadyRegistered(this IServiceCollection services)
    {
        if (services.Any(s => s.ServiceType == typeof(ConfigManager)))
        {
            throw new InvalidOperationException("Cocoar Configuration has already been registered. AddCocoarConfiguration should only be called once.");
        }
    }

    private static Dictionary<object, ServiceLifetime> ResolveLifetimeSelections(IEnumerable<ServiceLifetimeCapability<SetupDefinition>> lifetimeCapabilities)
    {
        var keyed = new Dictionary<object, ServiceLifetime>();
        foreach (var cap in lifetimeCapabilities)
        {
            keyed[cap.Key ?? ""] = cap.Lifetime;
        }
        return keyed;
    }

}
