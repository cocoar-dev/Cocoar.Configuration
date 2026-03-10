namespace Cocoar.Configuration.Flags;

/// <summary>
/// Fluent builder for registering feature flags classes with the DI container via
/// <see cref="ConfigManagerBuilderExtensions.UseFeatureFlags"/>.
/// </summary>
public sealed class FeatureFlagsSetupBuilder
{
    private readonly List<Type> _types = [];

    internal IReadOnlyList<Type> Types => _types;

    /// <summary>
    /// Registers a feature flags class for singleton DI registration.
    /// The class will be injected with <see cref="IFeatureFlagsRegistry"/> automatically.
    /// </summary>
    public FeatureFlagsSetupBuilder Register<T>() where T : FeatureFlags
    {
        _types.Add(typeof(T));
        return this;
    }
}
