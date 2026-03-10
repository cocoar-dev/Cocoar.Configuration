namespace Cocoar.Configuration.Flags;

/// <summary>
/// Registry for <see cref="FeatureFlags"/> class instances.
/// Provides a catalog of all registered feature flag classes in the application.
/// </summary>
/// <remarks>
/// <para>
/// The registry enables scenarios like:
/// </para>
/// <list type="bullet">
///   <item>Inventory of all feature flags in the application</item>
///   <item>Health checks for expired feature flags</item>
///   <item>Management UI (ConfigHub) displaying all flags</item>
/// </list>
/// </remarks>
public interface IFeatureFlagsRegistry
{
    /// <summary>
    /// Registers a <see cref="FeatureFlags"/> instance.
    /// </summary>
    /// <param name="featureFlags">The feature flags instance to register.</param>
    void Register(FeatureFlags featureFlags);

    /// <summary>
    /// Unregisters a <see cref="FeatureFlags"/> instance.
    /// </summary>
    /// <param name="featureFlags">The feature flags instance to unregister.</param>
    /// <returns>True if the instance was found and removed; otherwise false.</returns>
    bool Unregister(FeatureFlags featureFlags);

    /// <summary>
    /// Gets all registered <see cref="FeatureFlags"/> instances.
    /// </summary>
    IReadOnlyCollection<FeatureFlags> GetAll();

    /// <summary>
    /// Finds a registered <see cref="FeatureFlags"/> instance by type.
    /// </summary>
    /// <typeparam name="T">The type of feature flags to retrieve.</typeparam>
    /// <returns>The registered instance, or null if not found.</returns>
    T? Find<T>() where T : FeatureFlags;

    /// <summary>
    /// Gets all expired <see cref="FeatureFlags"/> instances.
    /// Useful for health checks and cleanup reminders.
    /// </summary>
    IReadOnlyCollection<FeatureFlags> GetExpired();
}
