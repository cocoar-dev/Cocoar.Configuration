namespace Cocoar.Configuration.Core;

/// <summary>
/// The container hook handed to service-backed (Layer-2, ADR-006) rule factories. Exposes the application
/// <see cref="System.IServiceProvider"/> and the activation signal as late-bound values, so a rule's provider
/// options factory — built eagerly, before the container exists — can resolve services lazily at recompute time,
/// once the host has started.
/// <para>
/// It is carried by <see cref="Cocoar.Configuration.Fluent.ServiceBackedProviderBuilder{T}"/> (no ambient state)
/// and is the <b>public seam a third-party provider package authors against</b>: in a <c>(sp, a) =&gt; …</c>
/// fluent overload, read <see cref="ServiceProvider"/> inside the options factory and gate the rule with
/// <c>.WithActivationGate(_ =&gt; context.IsActive)</c>. Constructed by <c>Cocoar.Configuration.DI</c>; using only
/// the BCL <see cref="System.IServiceProvider"/> keeps the No-DI core free of a DI-package dependency.
/// </para>
/// </summary>
public sealed class ServiceBackedRuleContext
{
    private readonly Func<bool> _isActive;
    private readonly Func<IServiceProvider> _serviceProvider;

    internal ServiceBackedRuleContext(Func<bool> isActive, Func<IServiceProvider> serviceProvider)
    {
        _isActive = isActive ?? throw new ArgumentNullException(nameof(isActive));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <summary>
    /// True once the container is built and the Layer-2 activation recompute may run. Gate a service-backed rule
    /// on it: <c>.WithActivationGate(_ =&gt; context.IsActive)</c> keeps the rule dormant until then.
    /// </summary>
    public bool IsActive => _isActive();

    /// <summary>
    /// The application root <see cref="IServiceProvider"/> — valid <b>only at recompute time</b> (once
    /// <see cref="IsActive"/> is true). Reading it earlier — e.g. in a fluent extension method body, which runs
    /// eagerly before the container exists — <b>throws</b> with guidance, instead of silently yielding null. Prefer
    /// <c>ServiceBackedProviderBuilder&lt;T&gt;.ServiceBacked((sp, a) =&gt; …)</c>, which hands you <c>sp</c> as a
    /// parameter of a factory the framework invokes at the right time, so this never bites. Resolve singletons /
    /// factories (e.g. <c>IHttpClientFactory</c>, <c>IDocumentStore</c>) and open short-lived units per read (§9).
    /// </summary>
    public IServiceProvider ServiceProvider => IsActive
        ? _serviceProvider()
        : throw new InvalidOperationException(
            "ServiceBackedRuleContext.ServiceProvider is only available at recompute time (after the host has " +
            "started). Read it inside the rule's factory — e.g. builder.ServiceBacked((sp, a) => …) — not in the " +
            "authoring method body, which runs eagerly before the container exists.");
}
