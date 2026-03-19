using Cocoar.Configuration.Flags.Internal;

namespace Cocoar.Configuration.Flags;

/// <summary>
/// Builder for registering entitlement classes using collection expressions.
/// Passed to the <c>UseEntitlements</c> extension method.
/// </summary>
/// <example>
/// <code>
/// .UseEntitlements(entitlements => [
///     entitlements.Register&lt;PlanEntitlements&gt;(),
///     entitlements.Register&lt;UsageEntitlements&gt;()
/// ])
/// </code>
/// </example>
public sealed class EntitlementsBuilder
{
    /// <summary>
    /// Registers an entitlement class.
    /// <para>
    /// Entitlement classes are <b>pure functions over reactive config state</b> -- they hold
    /// no per-request state and their only valid dependencies are
    /// <see cref="Reactive.IReactiveConfig{T}"/> instances, which are themselves singletons.
    /// Entitlements are therefore always registered as singletons.
    /// </para>
    /// Descriptor metadata is resolved from the source-generated <c>CocoarFlagsDescriptors</c>
    /// dictionary. If the generator has not run for this assembly, a minimal descriptor
    /// (no entitlements) is used as a fallback.
    /// </summary>
    public EntitlementRegistration Register<T>()
        where T : class
    {
        var descriptor = DescriptorLookup.GetEntitlementsDescriptor(typeof(T))
            ?? new EntitlementClassDescriptor(typeof(T), []);
        return new EntitlementRegistration(descriptor);
    }
}
