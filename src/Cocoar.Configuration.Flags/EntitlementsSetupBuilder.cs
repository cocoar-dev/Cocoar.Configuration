namespace Cocoar.Configuration.Flags;

/// <summary>
/// Fluent builder for registering entitlements classes with the DI container via
/// <see cref="ConfigManagerBuilderExtensions.UseEntitlements"/>.
/// </summary>
public sealed class EntitlementsSetupBuilder
{
    private readonly List<Type> _types = [];

    internal IReadOnlyList<Type> Types => _types;

    /// <summary>
    /// Registers an entitlements class for singleton DI registration.
    /// The class will be injected with <see cref="IEntitlementsRegistry"/> automatically.
    /// </summary>
    public EntitlementsSetupBuilder Register<T>() where T : Entitlements
    {
        _types.Add(typeof(T));
        return this;
    }
}
