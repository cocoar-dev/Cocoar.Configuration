using Cocoar.Configuration.Core;
using Cocoar.Configuration.Reactive;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.DI;

/// <summary>
/// Applies a service registration plan to an IServiceCollection.
/// Separates DI manipulation from interpretation logic.
/// </summary>
internal static class ServiceDescriptorEmitter
{
    /// <summary>
    /// Applies the registration plan to the service collection.
    /// </summary>
    public static void Emit(
        IServiceCollection services,
        Dictionary<Type, ServiceRegistrationInfo> plan)
    {
        // Explicit ordering for deterministic emission (belt-and-suspenders with planner sort)
        foreach (var (serviceType, value) in plan.OrderBy(kvp => kvp.Key.FullName))
        {
            EmitConfigService(services, serviceType, value);
            EmitReactiveService(services, serviceType);
        }
    }

    private static void EmitConfigService(
        IServiceCollection services,
        Type serviceType,
        ServiceRegistrationInfo info)
    {
        // Default registration (scoped) if not disabled and not overwritten
        if (info is { DisableDefault: false, OverwriteDefault: false })
        {
            services.Add(new ServiceDescriptor(
                serviceType,
                sp => sp.GetRequiredService<ConfigManager>().GetConfig(serviceType)!,
                ServiceLifetime.Scoped));
        }

        // Custom lifetime registrations (keyed or unkeyed)
        foreach (var (serviceKey, serviceLifetime) in info.ServiceLifetimes)
        {
            if (serviceKey is "")
            {
                services.Add(new ServiceDescriptor(
                    serviceType,
                    sp => sp.GetRequiredService<ConfigManager>().GetConfig(serviceType)!,
                    serviceLifetime));
            }
            else
            {
                services.Add(new ServiceDescriptor(
                    serviceType,
                    serviceKey,
                    (sp, _) => sp.GetRequiredService<ConfigManager>().GetConfig(serviceType)!,
                    serviceLifetime));
            }
        }
    }

    private static void EmitReactiveService(IServiceCollection services, Type serviceType)
    {
        var reactiveType = typeof(IReactiveConfig<>).MakeGenericType(serviceType);
        services.AddSingleton(reactiveType, sp =>
        {
            var mgr = sp.GetRequiredService<ConfigManager>();
            var method = mgr.GetType().GetMethod("GetReactiveConfig")!.MakeGenericMethod(serviceType);
            return method.Invoke(mgr, null)!;
        });
    }
}
