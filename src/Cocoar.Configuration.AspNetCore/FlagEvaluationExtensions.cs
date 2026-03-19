using System.Reflection;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Flags;
using Cocoar.Configuration.Flags.Internal;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.AspNetCore;

/// <summary>
/// Extension methods for mapping auto-generated REST evaluation endpoints for registered
/// feature flags and entitlements.
/// </summary>
public static class FlagEvaluationExtensions
{
    private static readonly Type FlagNoContextDef = typeof(FeatureFlag<>);
    private static readonly Type EntitlementNoContextDef = typeof(Entitlement<>);

    /// <summary>
    /// Maps REST evaluation endpoints for all registered feature flag classes.
    /// Returns a <see cref="RouteGroupBuilder"/> so callers can chain ASP.NET Core
    /// endpoint conventions such as authorization, rate limiting, or CORS.
    /// </summary>
    /// <param name="app">The endpoint route builder (e.g. <c>WebApplication</c>).</param>
    /// <param name="pathPrefix">URL prefix for all generated endpoints. Defaults to <c>/flags</c>.</param>
    /// <returns>
    /// A <see cref="RouteGroupBuilder"/> scoped to <paramref name="pathPrefix"/> for further
    /// configuration (e.g. <c>.RequireAuthorization()</c>).
    /// </returns>
    /// <example>
    /// <code>
    /// // Unsecured (development / internal only):
    /// app.MapFeatureFlagEndpoints();
    ///
    /// // Secured with an authorization policy:
    /// app.MapFeatureFlagEndpoints()
    ///    .RequireAuthorization("AdminPolicy");
    ///
    /// // GET  /flags/AppFeatureFlags/NewDashboardEnabled           -> { "value": true }
    /// // POST /flags/AppFeatureFlags/NewDashboardForUser            -> body { "userId": "beta_123" } -> { "value": true }
    /// </code>
    /// </example>
    public static RouteGroupBuilder MapFeatureFlagEndpoints(
        this IEndpointRouteBuilder app,
        string pathPrefix = "/flags")
    {
        var group = app.MapGroup(pathPrefix);

        var services = ((IApplicationBuilder)app).ApplicationServices;
        var configManager = services.GetRequiredService<ConfigManager>();

        if (configManager.FlagsSetup is not { } capability)
            return group;

        // GET endpoints — no-context flags: direct invocation, no resolver needed
        foreach (var registration in capability.Registrations)
        {
            var flagClassType = registration.Descriptor.Type;
            var className = flagClassType.Name;

            foreach (var prop in flagClassType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.PropertyType.IsGenericType) continue;
                if (prop.PropertyType.GetGenericTypeDefinition() != FlagNoContextDef) continue;

                var capturedProp = prop;
                var capturedType = flagClassType;
                group.MapGet($"{className}/{prop.Name}", (IServiceProvider sp) =>
                {
                    try
                    {
                        var flagClass = sp.GetRequiredService(capturedType);
                        var del = (Delegate)capturedProp.GetValue(flagClass)!;
                        return Results.Json(new { value = del.DynamicInvoke() });
                    }
                    catch (TargetInvocationException ex) when (ex.InnerException is not null)
                    {
                        return Results.Problem(
                            detail: ex.InnerException.Message,
                            title: "FeatureFlag evaluation failed",
                            statusCode: 500);
                    }
                });
            }
        }

        // POST endpoints — contextual flags: body deserialized to TRequest, then delegated to IFeatureFlagEvaluator.
        foreach (var (key, entry) in capability.EvaluationEntries)
        {
            var capturedKey = key;
            var requestType = entry.Resolver.RequestType;

            group.MapPost(capturedKey, async (HttpContext ctx, IFeatureFlagEvaluator evaluator) =>
            {
                var request = await ctx.Request.ReadFromJsonAsync(requestType, ctx.RequestAborted);
                if (request is null)
                    return Results.BadRequest("Unable to deserialize request body.");

                var result = await evaluator.EvaluateAsync(capturedKey, request, ctx.RequestAborted);
                return Results.Json(new { value = result });
            });
        }

        return group;
    }

    /// <summary>
    /// Maps REST evaluation endpoints for all registered entitlement classes.
    /// Returns a <see cref="RouteGroupBuilder"/> so callers can chain ASP.NET Core
    /// endpoint conventions such as authorization, rate limiting, or CORS.
    /// </summary>
    /// <param name="app">The endpoint route builder (e.g. <c>WebApplication</c>).</param>
    /// <param name="pathPrefix">URL prefix for all generated endpoints. Defaults to <c>/entitlements</c>.</param>
    /// <returns>
    /// A <see cref="RouteGroupBuilder"/> scoped to <paramref name="pathPrefix"/> for further
    /// configuration (e.g. <c>.RequireAuthorization()</c>).
    /// </returns>
    /// <example>
    /// <code>
    /// // Secured:
    /// app.MapEntitlementEndpoints()
    ///    .RequireAuthorization("AdminPolicy");
    ///
    /// // GET  /entitlements/PlanEntitlements/MaxUsers         -> { "value": 100 }
    /// // POST /entitlements/PlanEntitlements/MaxUsersForTenant -> body { "tenantId": "t_123" } -> { "value": 50 }
    /// </code>
    /// </example>
    public static RouteGroupBuilder MapEntitlementEndpoints(
        this IEndpointRouteBuilder app,
        string pathPrefix = "/entitlements")
    {
        var group = app.MapGroup(pathPrefix);

        var services = ((IApplicationBuilder)app).ApplicationServices;
        var configManager = services.GetRequiredService<ConfigManager>();

        if (configManager.EntitlementsSetup is not { } capability)
            return group;

        // GET endpoints — no-context entitlements: direct invocation, no resolver needed
        foreach (var registration in capability.Registrations)
        {
            var entitlementClassType = registration.Descriptor.Type;
            var className = entitlementClassType.Name;

            foreach (var prop in entitlementClassType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.PropertyType.IsGenericType) continue;
                if (prop.PropertyType.GetGenericTypeDefinition() != EntitlementNoContextDef) continue;

                var capturedProp = prop;
                var capturedType = entitlementClassType;
                group.MapGet($"{className}/{prop.Name}", (IServiceProvider sp) =>
                {
                    try
                    {
                        var entitlementClass = sp.GetRequiredService(capturedType);
                        var del = (Delegate)capturedProp.GetValue(entitlementClass)!;
                        return Results.Json(new { value = del.DynamicInvoke() });
                    }
                    catch (TargetInvocationException ex) when (ex.InnerException is not null)
                    {
                        return Results.Problem(
                            detail: ex.InnerException.Message,
                            title: "Entitlement evaluation failed",
                            statusCode: 500);
                    }
                });
            }
        }

        // POST endpoints — contextual entitlements: body deserialized to TRequest, then delegated to IEntitlementEvaluator.
        foreach (var (key, entry) in capability.EvaluationEntries)
        {
            var capturedKey = key;
            var requestType = entry.Resolver.RequestType;

            group.MapPost(capturedKey, async (HttpContext ctx, IEntitlementEvaluator evaluator) =>
            {
                var request = await ctx.Request.ReadFromJsonAsync(requestType, ctx.RequestAborted);
                if (request is null)
                    return Results.BadRequest("Unable to deserialize request body.");

                var result = await evaluator.EvaluateAsync(capturedKey, request, ctx.RequestAborted);
                return Results.Json(new { value = result });
            });
        }

        return group;
    }
}
