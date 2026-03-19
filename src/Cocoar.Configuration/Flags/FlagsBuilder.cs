using Cocoar.Configuration.Flags.Internal;

namespace Cocoar.Configuration.Flags;

/// <summary>
/// Builder for registering <see cref="FeatureFlags"/> subclasses using collection expressions.
/// Passed to the <c>UseFeatureFlags</c> extension method.
/// </summary>
/// <example>
/// <code>
/// .UseFeatureFlags(flags => [
///     flags.Register&lt;BillingFlags&gt;(),
///     flags.Register&lt;RolloutFlags&gt;()
/// ])
/// </code>
/// </example>
public sealed class FlagsBuilder
{
    /// <summary>
    /// Registers a <see cref="FeatureFlags"/> subclass.
    /// <para>
    /// Feature flag classes are <b>pure functions over reactive config state</b> -- they hold
    /// no per-request state and their only valid dependencies are
    /// <see cref="Reactive.IReactiveConfig{T}"/> instances. Any I/O required for contextual
    /// flag evaluation must live in an <see cref="IContextResolver{TRequest,TContext}"/>,
    /// not in the flag class. Flags are therefore always registered as singletons.
    /// </para>
    /// Descriptor metadata is resolved from the source-generated <c>CocoarFlagsDescriptors</c>
    /// dictionary. If the generator has not run for this assembly, a minimal descriptor
    /// (no flags, non-expired) is used as a fallback.
    /// </summary>
    public FlagRegistration Register<T>()
        where T : FeatureFlags
    {
        var descriptor = DescriptorLookup.GetFlagsDescriptor(typeof(T))
            ?? new FeatureFlagClassDescriptor(typeof(T), DateTimeOffset.MaxValue, []);
        return new FlagRegistration(descriptor);
    }
}
