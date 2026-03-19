namespace Cocoar.Configuration.Flags;

/// <summary>
/// Evaluates a contextual entitlement by key, hydrating the domain context via a registered
/// <see cref="IContextResolver{TRequest,TContext}"/>.
/// <para>
/// Entitlements are <b>permanent</b> — they represent what a user or tenant is allowed to do
/// based on their plan, license, or role. Use <see cref="IFeatureFlagEvaluator"/> for temporary
/// feature flags that should eventually be removed.
/// </para>
/// <para>
/// This service provides a second path for evaluating contextual entitlements alongside the direct
/// delegate call. Two equivalent ways to evaluate the same entitlement:
/// </para>
/// <list type="number">
///   <item>
///     <description>
///       <b>Direct</b> — resolve the entitlement class from DI and invoke the delegate:
///       <code>entitlements.MaxUsersForTenant(tenantContext)</code>
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Via evaluator</b> — pass a resolver request DTO and let the service hydrate context:
///       <code>await evaluator.EvaluateAsync("PlanEntitlements/MaxUsersForTenant", new TenantIdRequest("t_123"))</code>
///     </description>
///   </item>
/// </list>
/// <para>
/// The evaluator is registered as <b>Scoped</b> in DI so it resolves the entitlement class and resolver
/// from the current scope (per-request in web applications).
/// </para>
/// <para>
/// Key format: <c>"{EntitlementClassName}/{PropertyName}"</c> — e.g. <c>"PlanEntitlements/MaxUsersForTenant"</c>.
/// Keys are derived from the registered entitlement class name and property name.
/// </para>
/// </summary>
public interface IEntitlementEvaluator
{
    /// <summary>
    /// Returns <see langword="true"/> if the key maps to a contextual entitlement with a registered
    /// resolver. Returns <see langword="false"/> for unknown keys or entitlements with no resolver.
    /// </summary>
    bool CanEvaluate(string key);

    /// <summary>
    /// Evaluates the contextual entitlement identified by <paramref name="key"/> using
    /// <paramref name="resolverRequest"/> to hydrate the context.
    /// </summary>
    /// <param name="key">
    /// Entitlement key in the form <c>"{EntitlementClassName}/{PropertyName}"</c>.
    /// </param>
    /// <param name="resolverRequest">
    /// The resolver request DTO. Must be assignable to the <c>TRequest</c> type expected by
    /// the registered <see cref="IContextResolver{TRequest,TContext}"/>.
    /// </param>
    /// <param name="cancellationToken">Propagated to the resolver's <c>ResolveAsync</c> call.</param>
    /// <returns>The entitlement result as <see cref="object"/>.</returns>
    /// <exception cref="KeyNotFoundException">The key has no registered evaluation entry.</exception>
    Task<object?> EvaluateAsync(
        string key,
        object resolverRequest,
        CancellationToken cancellationToken = default);
}
