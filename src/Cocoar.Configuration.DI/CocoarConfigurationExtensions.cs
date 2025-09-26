using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Rules;
using Cocoar.Configuration.Reactive;

namespace Cocoar.Configuration.DI;


public static class CocoarConfigurationExtensions
{

    public static IServiceCollection AddCocoarConfiguration(
        this IServiceCollection services,
        ConfigManager configManager,
        Action<ServiceRegistrationOptions>? configureServices = null)
    {

        services.ThrowIfAlreadyRegistered();
        services.AddSingleton(configManager);

        services.AddSingleton<IConfigurationAccessor>(serviceProvider => 
            serviceProvider.GetRequiredService<ConfigManager>());

        var options = new ServiceRegistrationOptions();
        configureServices?.Invoke(options);
        RegisterConfigurationServices(services, configManager, options);

        return services;
    }

    public static IServiceCollection AddCocoarConfiguration(
        this IServiceCollection services,
        IEnumerable<ConfigRule> rules,
        IEnumerable<BindingSpec>? bindings = null,
        Action<ServiceRegistrationOptions>? configureServices = null,
        ILogger? logger = null,
        int debounceMilliseconds = 300)
    {

        services.ThrowIfAlreadyRegistered();

        var ruleList = rules.ToList();
        var bindingList = bindings?.ToList() ?? new List<BindingSpec>();

        var configManager = new ConfigManager(ruleList, bindingList, logger, debounceMilliseconds: debounceMilliseconds);
        configManager.Initialize();
        services.AddSingleton(configManager);

        services.AddSingleton<IConfigurationAccessor>(serviceProvider => 
            serviceProvider.GetRequiredService<ConfigManager>());

        var options = new ServiceRegistrationOptions();
        configureServices?.Invoke(options);
        RegisterConfigurationServices(services, configManager, options);

        return services;
    }

    private static void ThrowIfAlreadyRegistered(this IServiceCollection services)
    {
        if (services.Any(s => s.ServiceType == typeof(ConfigManager)))
        {
            throw new InvalidOperationException("Cocoar Configuration has already been registered. AddCocoarConfiguration should only be called once.");
        }
    }


    internal static void RegisterConfigurationServices(
        IServiceCollection services,
        ConfigManager configManager,
        ServiceRegistrationOptions options)
    {
        var explicitRegistrations = options.Register.Build().ToList();
        var explicitServiceTypes = new HashSet<(Type ServiceType, object? ServiceKey)>();
        var removedServiceTypes = options.Register.GetRemovals();

        foreach (var reg in explicitRegistrations)
        {
            explicitServiceTypes.Add((reg.ServiceType, reg.ServiceKey));
        }

        if (options.AutoRegisterReactiveConfigs)
        {
            foreach (var rule in configManager.Rules)
            {
                var concreteType = rule.ConcreteType;
                var reactiveConfigType = typeof(IReactiveConfig<>).MakeGenericType(concreteType);
                
                if (!explicitServiceTypes.Contains((reactiveConfigType, null)) && !removedServiceTypes.Contains((reactiveConfigType, null)))
                {
                    var descriptor = ServiceDescriptor.Singleton(
                        reactiveConfigType,
                        serviceProvider =>
                        {
                            var manager = serviceProvider.GetRequiredService<ConfigManager>();
                            var method = manager.GetType().GetMethod("GetReactiveConfig")!
                                .MakeGenericMethod(concreteType);
                            return method.Invoke(manager, null)!;
                        });
                    services.Add(descriptor);
                }
            }

            foreach (var binding in configManager.Bindings)
            {
                foreach (var interfaceType in binding.BoundInterfaces)
                {
                    var reactiveConfigType = typeof(IReactiveConfig<>).MakeGenericType(interfaceType);
                    
                    if (!explicitServiceTypes.Contains((reactiveConfigType, null)) && !removedServiceTypes.Contains((reactiveConfigType, null)))
                    {
                        var descriptor = ServiceDescriptor.Singleton(
                            reactiveConfigType,
                            serviceProvider =>
                            {
                                var manager = serviceProvider.GetRequiredService<ConfigManager>();
                                var method = manager.GetType().GetMethod("GetReactiveConfig")!
                                    .MakeGenericMethod(interfaceType);
                                return method.Invoke(manager, null)!;
                            });
                        services.Add(descriptor);
                    }
                }
            }

        }


        if (options.DefaultLifetime.HasValue)
        {
            foreach (var rule in configManager.Rules)
            {
                var ruleType = rule.ConcreteType;
                if (!explicitServiceTypes.Contains((ruleType, null)) && !removedServiceTypes.Contains((ruleType, null)))
                {
                    var descriptor = ServiceDescriptor.Describe(
                        ruleType,
                        _ => configManager.GetConfig(ruleType)!,
                        options.DefaultLifetime.Value);
                    services.Add(descriptor);
                }
            }
        }

        if (options.DefaultLifetime.HasValue)
        {
            foreach (var binding in configManager.Bindings)
            {
                foreach (var interfaceType in binding.BoundInterfaces)
                {
                    if (!explicitServiceTypes.Contains((interfaceType, null)) && !removedServiceTypes.Contains((interfaceType, null)))
                    {
                        var descriptor = ServiceDescriptor.Describe(
                            interfaceType,
                            _ => configManager.GetConfig(interfaceType)!,
                            options.DefaultLifetime.Value);
                        services.Add(descriptor);
                    }
                }
            }
        }

        foreach (var registration in explicitRegistrations)
        {
            var descriptor =
                registration.ServiceKey switch
            {
                null => ServiceDescriptor.Describe(
                    registration.ServiceType,
                    _ => configManager.GetConfig(registration.ServiceType)!,
                    registration.Lifetime),
                _ => ServiceDescriptor.DescribeKeyed(
                    registration.ServiceType,
                    registration.ServiceKey,
                    (_, _) => configManager.GetConfig(registration.ServiceType)!,
                    registration.Lifetime)
            };

            services.Add(descriptor);
        }
    }
}
