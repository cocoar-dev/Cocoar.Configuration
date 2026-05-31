using System.Reflection;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Flags;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.AspNetCore;

/// <summary>
/// Tenant-aware REST evaluation endpoints (ADR-005 §7): the same auto-generated endpoints as
/// <see cref="FlagEvaluationExtensions"/>, dimensioned by a <c>{tenant}</c> route segment. The handler warms the
/// tenant up (<c>EnsureTenantInitializedAsync</c>) and evaluates the flag/entitlement against THAT tenant's
/// effective configuration via <c>GetFeatureFlagsForTenant</c> / <c>GetEntitlementsForTenant</c>.
/// </summary>
public static class TenantFlagEvaluationExtensions
{
    private static readonly Type FlagNoContextDef = typeof(FeatureFlag<>);
    private static readonly Type EntitlementNoContextDef = typeof(Entitlement<>);
    private static readonly MethodInfo GetFeatureFlagsForTenantMethod =
        typeof(ConfigManagerFlagsExtensions).GetMethod(nameof(ConfigManagerFlagsExtensions.GetFeatureFlagsForTenant))!;
    private static readonly MethodInfo GetEntitlementsForTenantMethod =
        typeof(ConfigManagerFlagsExtensions).GetMethod(nameof(ConfigManagerFlagsExtensions.GetEntitlementsForTenant))!;

    /// <summary>
    /// Maps per-tenant GET endpoints for all registered no-context feature flags. Route shape (default prefix):
    /// <c>GET /tenants/{tenant}/flags/{FlagClass}/{FlagName}</c> → <c>{ "value": … }</c> for that tenant.
    /// </summary>
    public static RouteGroupBuilder MapTenantFeatureFlagEndpoints(
        this IEndpointRouteBuilder app,
        string pathPrefix = "/tenants/{tenant}/flags")
    {
        var group = app.MapGroup(pathPrefix);

        var configManager = ((IApplicationBuilder)app).ApplicationServices.GetRequiredService<ConfigManager>();
        if (configManager.FlagsSetup is not { } capability)
        {
            return group;
        }

        foreach (var registration in capability.Registrations)
        {
            MapTenantNoContextEndpoints(group, registration.Descriptor.Type, FlagNoContextDef,
                GetFeatureFlagsForTenantMethod, "FeatureFlag evaluation failed");
        }

        return group;
    }

    /// <summary>
    /// Maps per-tenant GET endpoints for all registered no-context entitlements. Route shape (default prefix):
    /// <c>GET /tenants/{tenant}/entitlements/{EntitlementClass}/{Name}</c> → <c>{ "value": … }</c> for that tenant.
    /// </summary>
    public static RouteGroupBuilder MapTenantEntitlementEndpoints(
        this IEndpointRouteBuilder app,
        string pathPrefix = "/tenants/{tenant}/entitlements")
    {
        var group = app.MapGroup(pathPrefix);

        var configManager = ((IApplicationBuilder)app).ApplicationServices.GetRequiredService<ConfigManager>();
        if (configManager.EntitlementsSetup is not { } capability)
        {
            return group;
        }

        foreach (var registration in capability.Registrations)
        {
            MapTenantNoContextEndpoints(group, registration.Descriptor.Type, EntitlementNoContextDef,
                GetEntitlementsForTenantMethod, "Entitlement evaluation failed");
        }

        return group;
    }

    private static void MapTenantNoContextEndpoints(
        RouteGroupBuilder group,
        Type classType,
        Type noContextDelegateDef,
        MethodInfo forTenantMethod,
        string failureTitle)
    {
        var className = classType.Name;

        foreach (var prop in classType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.PropertyType.IsGenericType) continue;
            if (prop.PropertyType.GetGenericTypeDefinition() != noContextDelegateDef) continue;

            var capturedProp = prop;
            var resolveForTenant = forTenantMethod.MakeGenericMethod(classType);

            group.MapGet($"{className}/{capturedProp.Name}", async (string tenant, ConfigManager mgr) =>
            {
                if (string.IsNullOrWhiteSpace(tenant))
                {
                    return Results.BadRequest("Tenant id is required.");
                }

                try
                {
                    await mgr.EnsureTenantInitializedAsync(tenant);
                    var instance = resolveForTenant.Invoke(null, [mgr, tenant])!;
                    var del = (Delegate)capturedProp.GetValue(instance)!;
                    return Results.Json(new { value = del.DynamicInvoke() });
                }
                catch (TargetInvocationException ex) when (ex.InnerException is not null)
                {
                    return Results.Problem(detail: ex.InnerException.Message, title: failureTitle, statusCode: 500);
                }
            });
        }
    }
}
