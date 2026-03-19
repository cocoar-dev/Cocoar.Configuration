using Cocoar.Configuration.DI.Capabilities;
using Cocoar.Configuration.Flags.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.DI;

/// <summary>
/// Represents one or more resolver registrations returned by <see cref="ResolverBuilder"/> methods.
/// Global resolvers carry a single registration; class-scoped registrations may carry multiple
/// (class-level + property-level resolvers).
/// Lifetime is customizable via <see cref="ServiceLifetimeCapability{T}"/> — consistent with
/// <c>ConcreteTypeSetup</c> and <c>ExposedTypeSetup</c>.
/// </summary>
public sealed class ResolverRegistration
{
    /// <summary>
    /// The individual resolver registrations. For <see cref="ResolverBuilder.Global{TResolver}"/>,
    /// this is a single entry. For <see cref="ResolverBuilder.For{TFlags}"/>, this may contain
    /// multiple entries (one per class-level or property-level resolver).
    /// </summary>
    internal IReadOnlyList<ContextResolverRegistration> Registrations { get; }

    /// <summary>
    /// The flag/entitlement class type this resolver set is associated with (null for global resolvers).
    /// </summary>
    internal Type? FlagClassType { get; }

    /// <summary>
    /// Lifetime capability set by <c>.AsSingleton()</c>, <c>.AsScoped()</c>, or <c>.AsTransient()</c>.
    /// Null = default (Scoped).
    /// </summary>
    internal ServiceLifetimeCapability<ResolverRegistration>? LifetimeCapability { get; set; }

    internal ResolverRegistration(ContextResolverRegistration registration)
    {
        Registrations = [registration];
        FlagClassType = null;
    }

    internal ResolverRegistration(IReadOnlyList<ContextResolverRegistration> registrations, Type flagClassType)
    {
        Registrations = registrations;
        FlagClassType = flagClassType;
    }

    /// <summary>
    /// Reads the DI lifetime from the capability, defaulting to Scoped.
    /// </summary>
    internal ServiceLifetime GetLifetime()
        => LifetimeCapability?.Lifetime ?? ServiceLifetime.Scoped;
}

/// <summary>
/// Extension methods on <see cref="ResolverRegistration"/> for customizing DI lifetime
/// via <see cref="ServiceLifetimeCapability{T}"/> — consistent with <c>ConcreteTypeSetup</c>
/// and <c>ExposedTypeSetup</c>.
/// </summary>
public static class ResolverRegistrationExtensions
{
    /// <summary>
    /// Registers resolvers in this registration as singletons in DI. Use when the resolver
    /// is stateless and has no scoped dependencies.
    /// </summary>
    public static ResolverRegistration AsSingleton(this ResolverRegistration registration)
    {
        registration.LifetimeCapability =
            new ServiceLifetimeCapability<ResolverRegistration>(ServiceLifetime.Singleton, null);
        return registration;
    }

    /// <summary>
    /// Registers resolvers in this registration as scoped in DI (one instance per request scope).
    /// This is the default lifetime; calling this method makes the intent explicit.
    /// </summary>
    public static ResolverRegistration AsScoped(this ResolverRegistration registration)
    {
        registration.LifetimeCapability =
            new ServiceLifetimeCapability<ResolverRegistration>(ServiceLifetime.Scoped, null);
        return registration;
    }

    /// <summary>
    /// Registers resolvers in this registration as transient in DI (new instance per resolution).
    /// </summary>
    public static ResolverRegistration AsTransient(this ResolverRegistration registration)
    {
        registration.LifetimeCapability =
            new ServiceLifetimeCapability<ResolverRegistration>(ServiceLifetime.Transient, null);
        return registration;
    }
}
