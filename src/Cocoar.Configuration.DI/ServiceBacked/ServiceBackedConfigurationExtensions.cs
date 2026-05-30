using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Rules;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.DI;

/// <summary>
/// The DI-package authoring surface for service-backed (Layer-2, ADR-006) configuration: rules whose provider
/// factories receive the application <see cref="IServiceProvider"/> (e.g. <c>FromStorage</c>,
/// <c>FromHttp((sp,a)=&gt;…)</c>). Layer 1 (<c>UseConfiguration</c>) stays eager and DI-free; Layer 2 is lazy
/// and container-owned, activated on host start.
/// </summary>
public static class ServiceBackedConfigurationExtensions
{
    /// <summary>
    /// Adds a <b>service-backed</b> (Layer-2) rule list whose factories may resolve services from the
    /// application container. These rules merge <em>after</em> the Layer-1 (<c>UseConfiguration</c>) rules
    /// (later precedence) and stay dormant until the host starts, then activate via a recompute that updates
    /// every live reactive view. <c>.TenantScoped()</c> composes here too (Marten-per-tenant = tenant gate +
    /// sp gate).
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddCocoarConfiguration(c => c
    ///     .UseConfiguration(rules => [
    ///         rules.For&lt;LogConfig&gt;().FromFile("appsettings.json")
    ///     ])
    ///     .UseServiceBackedConfiguration(rules => [
    ///         rules.For&lt;LogConfig&gt;().FromHttp(
    ///             (sp, a) => sp.GetRequiredService&lt;IHttpClientFactory&gt;().CreateClient("cocoar-config"),
    ///             "logging.json", pollInterval: TimeSpan.FromSeconds(30)),
    ///         rules.For&lt;TenantSettings&gt;().FromStorage(
    ///             (sp, a) => new MartenConfigBackend(sp.GetRequiredService&lt;IDocumentStore&gt;(), a.Tenant))
    ///             .TenantScoped()
    ///     ]));
    /// </code>
    /// </example>
    /// <param name="builder">The configuration builder.</param>
    /// <param name="rules">A function that builds the service-backed rules using the fluent API; its sp-using
    /// factories (<c>FromStorage</c>, <c>FromHttp((sp,a)=&gt;…)</c>) read the container at recompute time.</param>
    public static ConfigManagerBuilder UseServiceBackedConfiguration(
        this ConfigManagerBuilder builder,
        Func<ServiceBackedRulesBuilder, ConfigRule[]> rules)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(rules);

        var manager = ConfigManagerBuilder.GetManager(builder);
        var holder = ServiceBackedConfigurationCoordinator.GetOrCreateHolder(manager);

        // The context exposes the activation signal + provider as late-bound values over the shared holder. It is
        // carried by the ServiceBackedProviderBuilder<T> handed to each For<T>() and read by the sp-using overloads
        // (FromStorage / FromHttp((sp,a)=>…) / third-party ones) — no ambient state.
        var context = new ServiceBackedRuleContext(
            isActive: () => holder.HasServiceProvider,
            serviceProvider: () => holder.ServiceProvider!);

        var layer2Rules = rules(new ServiceBackedRulesBuilder(context)) ?? [];

        builder.AddServiceBackedRules(layer2Rules);
        return builder;
    }

    /// <summary>
    /// Manually activates service-backed configuration for hosts that do not run an <c>IHost</c> (e.g. a console
    /// app that builds its own <see cref="IServiceProvider"/>). Pass the <b>root</b> provider. No-op if no
    /// service-backed rules were registered, and idempotent with the automatic hosted-service activation.
    /// </summary>
    /// <param name="serviceProvider">The application root service provider.</param>
    public static Task ActivateServiceBackedConfigurationAsync(this IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var holder = serviceProvider.GetService<ServiceProviderHolder>();
        if (holder is null)
        {
            return Task.CompletedTask; // no service-backed configuration was registered — nothing to activate
        }

        // Always use the ROOT provider for the holder, even if a scoped provider was passed: the holder's sp is
        // read on every later recompute/poll, so capturing a scope that disposes would fault Layer-2 reads (§9).
        var root = serviceProvider.GetService<RootServiceProviderAccessor>()?.Root ?? serviceProvider;
        var manager = serviceProvider.GetRequiredService<ConfigManager>();
        return ServiceBackedConfigurationCoordinator.ActivateAsync(
            manager, holder, manager.ServiceBackedLayerStartIndex, root);
    }
}
