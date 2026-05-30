using Cocoar.Configuration.Core;

namespace Cocoar.Configuration.Fluent;

/// <summary>
/// The rule-list builder for <c>UseServiceBackedConfiguration</c> (Layer 2, ADR-006). Start with
/// <see cref="For{T}"/> to author a service-backed rule whose factories receive the application
/// <see cref="System.IServiceProvider"/>. Mirrors <see cref="RulesBuilder"/> but yields the DI-aware
/// <see cref="ServiceBackedProviderBuilder{T}"/>.
/// </summary>
public sealed class ServiceBackedRulesBuilder
{
    private readonly ServiceBackedRuleContext _context;

    internal ServiceBackedRulesBuilder(ServiceBackedRuleContext context)
        => _context = context ?? throw new ArgumentNullException(nameof(context));

    /// <summary>Start a type-safe service-backed rule for configuration type <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The configuration type this rule will populate.</typeparam>
    public ServiceBackedProviderBuilder<T> For<T>() where T : class => new(_context);
}
