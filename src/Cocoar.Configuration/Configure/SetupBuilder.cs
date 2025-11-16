using Cocoar.Capabilities;
using Cocoar.Configuration.Core;

namespace Cocoar.Configuration.Configure;




public abstract class SetupDefinition(ConfigManagerCapabilityScope capabilityScope) {
    protected ConfigManagerCapabilityScope CapabilityScope { get; } = capabilityScope;
    internal abstract SetupDefinition Build();

    public static ConfigManagerCapabilityScope GetCapabilityScopeFor(SetupDefinition builder) => builder.CapabilityScope;

    public static Composer GetComposer(SetupDefinition builder) =>
        GetCapabilityScopeFor(builder).Composers.GetRequired(builder);
}

/// <summary>
/// Configures how types are registered in the DI container.
/// All concrete types are Scoped by default. Use ConcreteType&lt;T&gt;().ExposeAs&lt;IInterface&gt;() to inject via interfaces.
/// </summary>
public sealed class SetupBuilder(ConfigManagerCapabilityScope capabilityScope)
{
    private readonly ConfigManagerCapabilityScope _capabilityScope = capabilityScope;

    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>
    /// Configure registration behavior for a concrete configuration type.
    /// By default, both the concrete type and any exposed interfaces are registered as Scoped.
    /// </summary>
    public ConcreteTypeSetup<T> ConcreteType<T>() where T : class => new(_capabilityScope);

    /// <summary>
    /// Configure deserialization mapping for an interface type.
    /// Use this when your configuration classes have interface-typed properties that need to be
    /// deserialized from JSON/environment variables. Specify which concrete type to instantiate.
    /// </summary>
    public InterfaceTypeSetup<TInterface> Interface<TInterface>() where TInterface : class => new(_capabilityScope);

    public static ConfigManagerCapabilityScope GetCapabilityScopeFor(SetupBuilder builder) => builder._capabilityScope;
}
