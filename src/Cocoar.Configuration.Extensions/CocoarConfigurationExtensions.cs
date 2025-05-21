using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Cocoar.Configuration.Extensions
{
    public static class CocoarConfigurationExtensions
    {
        /// <summary>
        /// Registers the ConfigManager and all requested config types with DI.
        /// </summary>
        public static IServiceCollection AddCocoarConfiguration(
            this IServiceCollection services,
            IEnumerable<ConfigRule> rules)
        {
            // 1. Add the ConfigManager as a singleton
            var ruleList = rules.ToList();
            services.AddSingleton(sp => new ConfigManager(sp, ruleList).Initialize());

            // 2. Register each config type (last rule wins for lifetime)
            var types = ruleList.Select(r => r.ConfigContract).Distinct();

            foreach (var type in types)
            {
                var lastRule = ruleList.Last(r => r.ConfigContract == type);
                var lifetime = lastRule.Lifetime ?? ConfigLifetime.Scoped;

                // Factory resolves config by type
                Func<IServiceProvider, object?> factory =
                    sp => sp.GetRequiredService<ConfigManager>().GetConfig(type);

                switch (lifetime)
                {
                    case ConfigLifetime.Singleton:
                        services.AddSingleton(type, factory);
                        break;
                    case ConfigLifetime.Transient:
                        services.AddTransient(type, factory);
                        break;
                    default:
                        services.AddScoped(type, factory);
                        break;
                }
            }

            return services;
        }

        /// <summary>
        /// Overload for params usage (convenient for app code)
        /// </summary>
        public static IServiceCollection AddCocoarConfiguration(
            this IServiceCollection services,
            params ConfigRule[] rules)
            => AddCocoarConfiguration(services, rules.AsEnumerable());
    }
}
