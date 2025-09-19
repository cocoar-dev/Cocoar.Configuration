using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cocoar.Configuration.DI;

/// <summary>
/// Dependency injection extensions for Cocoar.Configuration.
/// </summary>
public static class CocoarConfigurationExtensions
{
    /// <summary>
    /// Registers a pre-built ConfigManager with the dependency injection container.
    /// Use this overload when you need full control over ConfigManager construction.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configManager">Pre-built ConfigManager instance</param>
    /// <param name="configureServices">Optional service registration configuration</param>
    /// <returns>The service collection for method chaining</returns>
    public static IServiceCollection AddCocoarConfiguration(
        this IServiceCollection services,
        ConfigManager configManager,
        Action<ServiceRegistrationOptions>? configureServices = null)
    {
        // Validate that no duplicate services are registered
        services.ThrowIfAlreadyRegistered();

        // Register the provided ConfigManager instance
        services.AddSingleton(configManager);

        // Register IConfigurationAccessor interface for dependency injection
        services.AddSingleton<IConfigurationAccessor>(serviceProvider => 
            serviceProvider.GetRequiredService<ConfigManager>());

        // Always register configuration services - use defaults if no options provided
        var options = new ServiceRegistrationOptions();
        configureServices?.Invoke(options);
        RegisterConfigurationServices(services, configManager, options);

        return services;
    }

    /// <summary>
    /// Registers the ConfigManager with the dependency injection container.
    /// Uses the same API as ConfigManager for consistency.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="rules">Configuration rules defining where configurations come from</param>
    /// <param name="bindings">Binding specifications defining interface mappings</param>
    /// <param name="configureServices">Optional service registration configuration</param>
    /// <param name="logger">Optional logger for ConfigManager</param>
    /// <param name="debounceMilliseconds">Debounce time for configuration change notifications</param>
    /// <returns>The service collection for method chaining</returns>
    public static IServiceCollection AddCocoarConfiguration(
        this IServiceCollection services,
        IEnumerable<ConfigRule> rules,
        IEnumerable<BindingSpec>? bindings = null,
        Action<ServiceRegistrationOptions>? configureServices = null,
        ILogger? logger = null,
        int debounceMilliseconds = 300)
    {
        // Validate that no duplicate services are registered
        services.ThrowIfAlreadyRegistered();

        var ruleList = rules.ToList();
        var bindingList = bindings?.ToList() ?? new List<BindingSpec>();

        // Create and register ConfigManager (same as core API)
        var configManager = new ConfigManager(ruleList, bindingList, logger, debounceMilliseconds: debounceMilliseconds);
        configManager.Initialize();
        services.AddSingleton(configManager);

        // Register IConfigurationAccessor interface for dependency injection
        services.AddSingleton<IConfigurationAccessor>(serviceProvider => 
            serviceProvider.GetRequiredService<ConfigManager>());

        // Always register configuration services - use defaults if no options provided
        var options = new ServiceRegistrationOptions();
        configureServices?.Invoke(options);
        RegisterConfigurationServices(services, configManager, options);

        return services;
    }

    /// <summary>
    /// Validates that Cocoar configuration services haven't been registered yet.
    /// </summary>
    private static void ThrowIfAlreadyRegistered(this IServiceCollection services)
    {
        if (services.Any(s => s.ServiceType == typeof(ConfigManager)))
        {
            throw new InvalidOperationException("Cocoar Configuration has already been registered. AddCocoarConfiguration should only be called once.");
        }
    }

    /// <summary>
    /// Registers configuration services in the DI container based on service registrations.
    /// Automatically registers all rule types and binding interfaces with default lifetime,
    /// then applies explicit overrides from the options.
    /// </summary>
    internal static void RegisterConfigurationServices(
        IServiceCollection services,
        ConfigManager configManager,
        ServiceRegistrationOptions options)
    {
        var explicitRegistrations = options.Register.Build().ToList();
        var explicitServiceTypes = new HashSet<(Type ServiceType, object? ServiceKey)>();
        var removedServiceTypes = options.Register.GetRemovals();

        // Track explicitly registered services to avoid duplicates
        foreach (var reg in explicitRegistrations)
        {
            explicitServiceTypes.Add((reg.ServiceType, reg.ServiceKey));
        }

        // Auto-register IReactiveConfig<T> for all configuration types as Singletons (unless disabled)
        if (options.AutoRegisterReactiveConfigs)
        {
            // Register IReactiveConfig<T> for all rule types
            foreach (var rule in configManager.Rules)
            {
                var concreteType = rule.ConcreteType;
                var reactiveConfigType = typeof(IReactiveConfig<>).MakeGenericType(concreteType);
                
                if (!explicitServiceTypes.Contains((reactiveConfigType, null)) && !removedServiceTypes.Contains((reactiveConfigType, null)))
                {
                    // Use reflection to call the generic GetReactiveConfig method
                    var descriptor = ServiceDescriptor.Singleton(
                        reactiveConfigType,
                        serviceProvider =>
                        {
                            var configManager = serviceProvider.GetRequiredService<ConfigManager>();
                            var method = configManager.GetType().GetMethod("GetReactiveConfig")!
                                .MakeGenericMethod(concreteType);
                            return method.Invoke(configManager, null)!;
                        });
                    services.Add(descriptor);
                }
            }

            // Register IReactiveConfig<T> for all binding interfaces
            foreach (var binding in configManager.Bindings)
            {
                foreach (var interfaceType in binding.BoundInterfaces)
                {
                    var reactiveConfigType = typeof(IReactiveConfig<>).MakeGenericType(interfaceType);
                    
                    if (!explicitServiceTypes.Contains((reactiveConfigType, null)) && !removedServiceTypes.Contains((reactiveConfigType, null)))
                    {
                        // Use reflection to call the generic GetReactiveConfig method
                        var descriptor = ServiceDescriptor.Singleton(
                            reactiveConfigType,
                            serviceProvider =>
                            {
                                var configManager = serviceProvider.GetRequiredService<ConfigManager>();
                                var method = configManager.GetType().GetMethod("GetReactiveConfig")!
                                    .MakeGenericMethod(interfaceType);
                                return method.Invoke(configManager, null)!;
                            });
                        services.Add(descriptor);
                    }
                }
            }
        }

        // Auto-register rule types with default lifetime (if default lifetime is set and not explicitly removed)
        if (options.DefaultLifetime.HasValue)
        {
            foreach (var rule in configManager.Rules)
            {
                var ruleType = rule.ConcreteType;
                if (!explicitServiceTypes.Contains((ruleType, null)) && !removedServiceTypes.Contains((ruleType, null)))
                {
                    var descriptor = ServiceDescriptor.Describe(
                        ruleType,
                        serviceProvider => configManager.GetConfig(ruleType)!,
                        options.DefaultLifetime.Value);
                    services.Add(descriptor);
                }
            }
        }

        // Auto-register binding interfaces with default lifetime (if default lifetime is set and not explicitly removed)
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
                            serviceProvider => configManager.GetConfig(interfaceType)!,
                            options.DefaultLifetime.Value);
                        services.Add(descriptor);
                    }
                }
            }
        }

        // Register explicit overrides (these take precedence over defaults)
        foreach (var registration in explicitRegistrations)
        {
            ServiceDescriptor descriptor;
            
            // Regular config registrations
            descriptor = registration.ServiceKey switch
            {
                null => ServiceDescriptor.Describe(
                    registration.ServiceType,
                    serviceProvider => configManager.GetConfig(registration.ServiceType)!,
                    registration.Lifetime),
                _ => ServiceDescriptor.DescribeKeyed(
                    registration.ServiceType,
                    registration.ServiceKey,
                    (serviceProvider, key) => configManager.GetConfig(registration.ServiceType)!,
                    registration.Lifetime)
            };

            services.Add(descriptor);
        }
    }
}
