using Cocoar.Configuration.Flags.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.Flags;

/// <summary>
/// Fluent builder for registering <see cref="Entitlements"/> subclasses.
/// Passed to the <c>UseEntitlements</c> extension method.
/// </summary>
public sealed class EntitlementsSetupBuilder
{
    private readonly List<EntitlementRegistration> _registrations = new();

    /// <summary>
    /// Registers an <see cref="Entitlements"/> subclass with the specified DI lifetime.
    /// Descriptor metadata is resolved from the source-generated <c>CocoarFlagsDescriptors</c>
    /// dictionary. If the generator has not run for this assembly, a minimal descriptor
    /// (no entitlements) is used as a fallback.
    /// </summary>
    public EntitlementsSetupBuilder Register<T>(ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where T : Entitlements
    {
        var descriptor = DescriptorLookup.GetEntitlementsDescriptor(typeof(T))
            ?? new EntitlementClassDescriptor(typeof(T), []);
        _registrations.Add(new EntitlementRegistration(descriptor, lifetime));
        return this;
    }

    internal IReadOnlyList<EntitlementRegistration> Build() => _registrations.AsReadOnly();
}

internal sealed record EntitlementRegistration(EntitlementClassDescriptor Descriptor, ServiceLifetime Lifetime);
