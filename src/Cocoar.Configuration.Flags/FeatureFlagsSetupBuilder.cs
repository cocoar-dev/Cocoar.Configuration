using Cocoar.Configuration.Flags.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.Configuration.Flags;

/// <summary>
/// Fluent builder for registering <see cref="FeatureFlags"/> subclasses.
/// Passed to the <c>UseFeatureFlags</c> extension method.
/// </summary>
public sealed class FeatureFlagsSetupBuilder
{
    private readonly List<FlagRegistration> _registrations = new();

    /// <summary>
    /// Registers a <see cref="FeatureFlags"/> subclass with the specified DI lifetime.
    /// Descriptor metadata is resolved from the source-generated <c>CocoarFlagsDescriptors</c>
    /// dictionary. If the generator has not run for this assembly, a minimal descriptor
    /// (no flags, non-expired) is used as a fallback.
    /// </summary>
    public FeatureFlagsSetupBuilder Register<T>(ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where T : FeatureFlags
    {
        var descriptor = DescriptorLookup.GetFlagsDescriptor(typeof(T))
            ?? new FeatureFlagClassDescriptor(typeof(T), DateTimeOffset.MaxValue, []);
        _registrations.Add(new FlagRegistration(descriptor, lifetime));
        return this;
    }

    internal IReadOnlyList<FlagRegistration> Build() => _registrations.AsReadOnly();
}

internal sealed record FlagRegistration(FeatureFlagClassDescriptor Descriptor, ServiceLifetime Lifetime);
