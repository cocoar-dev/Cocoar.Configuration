using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cocoar.Configuration.AspNetCore;

/// <summary>
/// Registration for the scoped per-request tenant configuration adapter (ADR-006 §11).
/// </summary>
public static class TenantReactiveConfigExtensions
{
    /// <summary>
    /// Registers the scoped <see cref="ITenantReactiveConfig{T}"/> adapter so scoped/transient consumers can
    /// inject the current request's tenant configuration. Leaves the singleton <c>IReactiveConfig&lt;T&gt;</c>
    /// registration untouched (the §11 trap).
    /// <para>
    /// The application must also register a scoped <see cref="ITenantContext"/> — either its own, or the default
    /// registered here when <paramref name="tenantResolver"/> is supplied. Tenants must be initialized
    /// (e.g. <c>EnsureTenantInitializedAsync</c> in middleware) before the adapter is used in a request.
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="tenantResolver">Optional resolver of the tenant id from the current <see cref="HttpContext"/>
    /// (a claim, header, or route value). When provided, a default scoped <see cref="ITenantContext"/> and
    /// <see cref="IHttpContextAccessor"/> are registered.</param>
    public static IServiceCollection AddCocoarTenantReactiveConfig(
        this IServiceCollection services,
        Func<HttpContext, string?>? tenantResolver = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped(typeof(ITenantReactiveConfig<>), typeof(TenantReactiveConfig<>));

        if (tenantResolver is not null)
        {
            services.AddHttpContextAccessor();
            services.TryAddScoped<ITenantContext>(sp =>
            {
                var httpContext = sp.GetRequiredService<IHttpContextAccessor>().HttpContext;
                return new DelegateTenantContext(httpContext is null ? null : tenantResolver(httpContext));
            });
        }

        return services;
    }
}

/// <summary>An <see cref="ITenantContext"/> carrying a pre-resolved tenant id (used by the default resolver path).</summary>
internal sealed class DelegateTenantContext : ITenantContext
{
    public DelegateTenantContext(string? current) => Current = current;

    public string? Current { get; }
}
