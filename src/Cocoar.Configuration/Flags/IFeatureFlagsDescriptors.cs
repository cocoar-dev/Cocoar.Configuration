namespace Cocoar.Configuration.Flags;

/// <summary>
/// Read-only catalog of registered feature flag classes.
/// Populated at startup from source-generator output — no runtime reflection required.
/// </summary>
/// <remarks>
/// <para>
/// Use this to:
/// </para>
/// <list type="bullet">
///   <item>Inventory all feature flags in the application</item>
///   <item>Drive health checks for expired feature flag classes</item>
///   <item>Populate management UI (ConfigHub) with flag names and descriptions</item>
/// </list>
/// </remarks>
public interface IFeatureFlagsDescriptors
{
    /// <summary>All registered feature flag class descriptors.</summary>
    IReadOnlyList<FeatureFlagClassDescriptor> All { get; }

    /// <summary>
    /// Descriptors whose class-level <c>ExpiresAt</c> has passed.
    /// When non-empty, the health status is reported as <c>Degraded</c>.
    /// </summary>
    IReadOnlyList<FeatureFlagClassDescriptor> Expired { get; }
}
