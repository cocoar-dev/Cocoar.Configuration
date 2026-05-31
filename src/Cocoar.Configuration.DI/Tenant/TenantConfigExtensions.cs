using Cocoar.Configuration.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cocoar.Configuration.DI;

/// <summary>
/// Registrations for per-request tenant configuration consumption (ADR-006 §11): the scoped
/// <see cref="ITenantReactiveConfig{T}"/> adapter, and resolving the current <see cref="ITenantContext"/>
/// from a DI service. Both are plain DI registrations — no ASP.NET dependency.
/// </summary>
public static class TenantConfigExtensions
{
    /// <summary>
    /// Registers the scoped <see cref="ITenantReactiveConfig{T}"/> adapter so scoped/transient consumers can
    /// inject the current request's tenant configuration. Leaves the singleton <c>IReactiveConfig&lt;T&gt;</c>
    /// registration untouched (the §11 trap). The app must ALSO register a scoped <see cref="ITenantContext"/> —
    /// e.g. via <see cref="AddCocoarTenantResolver{TService}"/> or its own.
    /// </summary>
    public static IServiceCollection AddCocoarTenantReactiveConfig(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped(typeof(ITenantReactiveConfig<>), typeof(TenantReactiveConfig<>));
        return services;
    }

    /// <summary>
    /// Registers a scoped <see cref="ITenantContext"/> that resolves the current tenant from a DI service
    /// <typeparamref name="TService"/> via <paramref name="selector"/>. The selector is evaluated lazily on
    /// every <see cref="ITenantContext.Current"/> access, so it reflects the tenant once it is known for the
    /// scope (e.g. after auth middleware).
    /// <para>
    /// The app already has a tenant service — point at it instead of writing an <see cref="ITenantContext"/>:
    /// <code>
    /// services.AddCocoarTenantResolver&lt;ApplicationTenantService&gt;(s => s.TenantId);
    /// </code>
    /// For HTTP, use <c>IHttpContextAccessor</c> as the service (no ASP.NET-specific API needed):
    /// <code>
    /// services.AddHttpContextAccessor();
    /// services.AddCocoarTenantResolver&lt;IHttpContextAccessor&gt;(a => a.HttpContext?.User.FindFirst("tenant")?.Value);
    /// </code>
    /// When the tenant comes from more than one service, use <c>IServiceProvider</c> as the service:
    /// <code>
    /// services.AddCocoarTenantResolver&lt;IServiceProvider&gt;(sp => /* combine services */);
    /// </code>
    /// </para>
    /// </summary>
    public static IServiceCollection AddCocoarTenantResolver<TService>(
        this IServiceCollection services,
        Func<TService, string?> selector)
        where TService : notnull
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(selector);

        services.TryAddScoped<ITenantContext>(sp =>
            new DelegateTenantContext(() => selector(sp.GetRequiredService<TService>())));
        return services;
    }
}

/// <summary>
/// An <see cref="ITenantContext"/> whose <see cref="Current"/> defers to a delegate, re-evaluated on every
/// access — so the tenant can become known after the scope starts (e.g. post-auth-middleware).
/// </summary>
public sealed class DelegateTenantContext : ITenantContext
{
    private readonly Func<string?> _resolve;

    /// <summary>Creates a context whose <see cref="Current"/> calls <paramref name="resolve"/> on each access.</summary>
    public DelegateTenantContext(Func<string?> resolve)
        => _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));

    /// <inheritdoc />
    public string? Current => _resolve();
}
