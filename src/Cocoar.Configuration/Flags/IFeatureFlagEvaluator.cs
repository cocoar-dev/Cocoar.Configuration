namespace Cocoar.Configuration.Flags;

/// <summary>
/// Evaluates a contextual feature flag by key, hydrating the domain context via a registered
/// <see cref="IContextResolver{TRequest,TContext}"/>.
/// <para>
/// Feature flags are <b>temporary</b> — they represent in-progress rollouts or experiments
/// that should eventually be removed or converted to entitlements.
/// Use <see cref="IEntitlementEvaluator"/> for permanent capability checks.
/// </para>
/// <para>
/// This service provides a second path for evaluating contextual flags alongside the direct
/// delegate call. Two equivalent ways to evaluate the same flag:
/// </para>
/// <list type="number">
///   <item>
///     <description>
///       <b>Direct</b> — resolve the flag class from DI and invoke the delegate:
///       <code>flags.NewDashboardForUser(userContext)</code>
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Via evaluator</b> — pass a resolver request DTO and let the service hydrate context:
///       <code>await evaluator.EvaluateAsync("AppFeatureFlags/NewDashboardForUser", new UserIdRequest("123"))</code>
///     </description>
///   </item>
/// </list>
/// <para>
/// The evaluator is registered as <b>Scoped</b> in DI so it resolves the flag class and resolver
/// from the current scope (per-request in web applications).
/// </para>
/// <para>
/// Key format: <c>"{FlagClassName}/{PropertyName}"</c> — e.g. <c>"AppFeatureFlags/NewDashboardForUser"</c>.
/// Keys are derived from the registered flag class name and property name.
/// </para>
/// </summary>
public interface IFeatureFlagEvaluator
{
    /// <summary>
    /// Returns <see langword="true"/> if the key maps to a contextual feature flag with a registered
    /// resolver. Returns <see langword="false"/> for unknown keys or flags with no resolver.
    /// </summary>
    bool CanEvaluate(string key);

    /// <summary>
    /// Evaluates the contextual feature flag identified by <paramref name="key"/> using
    /// <paramref name="resolverRequest"/> to hydrate the context.
    /// </summary>
    /// <param name="key">
    /// FeatureFlag key in the form <c>"{FlagClassName}/{PropertyName}"</c>.
    /// </param>
    /// <param name="resolverRequest">
    /// The resolver request DTO. Must be assignable to the <c>TRequest</c> type expected by
    /// the registered <see cref="IContextResolver{TRequest,TContext}"/>.
    /// </param>
    /// <param name="cancellationToken">Propagated to the resolver's <c>ResolveAsync</c> call.</param>
    /// <returns>The flag result as <see cref="object"/>.</returns>
    /// <exception cref="KeyNotFoundException">The key has no registered evaluation entry.</exception>
    Task<object?> EvaluateAsync(
        string key,
        object resolverRequest,
        CancellationToken cancellationToken = default);
}
