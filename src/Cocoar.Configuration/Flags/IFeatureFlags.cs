namespace Cocoar.Configuration.Flags;

/// <summary>
/// Marker interface for feature flag classes with typed configuration.
/// Implement this on a partial class — the source generator produces the constructor and Config property.
/// </summary>
/// <typeparam name="TConfig">The configuration type (or value tuple of types) this flag class reads from.</typeparam>
public interface IFeatureFlags<TConfig> where TConfig : class
{
}
