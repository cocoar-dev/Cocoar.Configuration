namespace Cocoar.Configuration.Flags;

/// <summary>
/// Descriptor-based registry for <see cref="FeatureFlags"/> classes.
/// Populated at startup via <c>UseFeatureFlags(f =&gt; f.Register&lt;T&gt;())</c>.
/// </summary>
/// <remarks>
/// <para>
/// The registry enables scenarios like:
/// </para>
/// <list type="bullet">
///   <item>Inventory of all feature flags in the application</item>
///   <item>Health checks for expired feature flag classes</item>
///   <item>Management UI (ConfigHub) displaying all flags</item>
/// </list>
/// </remarks>
public interface IFeatureFlagsRegistry
{
    /// <summary>
    /// Registers a <see cref="FeatureFlagClassDescriptor"/> for a feature flags class.
    /// </summary>
    void RegisterDescriptor(FeatureFlagClassDescriptor descriptor);

    /// <summary>
    /// Gets all registered <see cref="FeatureFlagClassDescriptor"/> instances.
    /// </summary>
    IReadOnlyCollection<FeatureFlagClassDescriptor> GetDescriptors();

    /// <summary>
    /// Gets all <see cref="FeatureFlagClassDescriptor"/> instances whose class-level <c>ExpiresAt</c> has passed.
    /// </summary>
    IReadOnlyCollection<FeatureFlagClassDescriptor> GetExpiredDescriptors();
}
