using Cocoar.Configuration.Core;
using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Rules;

namespace Cocoar.Configuration.ConfigHub;

/// <summary>
/// A typed class-object rule. Selection is resolved from its type or alias and sent to
/// ConfigHub, so an independent Select or MountAt cannot change that contract.
/// </summary>
public sealed class ConfigHubRuleBuilder : IConfigRuleBuilder
{
    private readonly ProviderRuleBuilder<ConfigHubProvider, ConfigHubProviderOptions, ConfigHubProviderQueryOptions> _builder;

    internal ConfigHubRuleBuilder(
        Func<IConfigurationAccessor, ConfigHubProviderOptions> instanceFactory,
        Func<IConfigurationAccessor, ConfigHubProviderQueryOptions> queryFactory,
        Type type) => _builder = new(instanceFactory, queryFactory, type);

    /// <summary>Fails the configuration pass if this remote class cannot be loaded.</summary>
    public ConfigHubRuleBuilder Required(bool value = true) { _builder.Required(value); return this; }

    /// <summary>Reads this rule only when its predicate is true.</summary>
    public ConfigHubRuleBuilder When(Func<IConfigurationAccessor, bool> predicate) { _builder.When(predicate); return this; }

    /// <summary>Reads this rule only in a tenant configuration pass.</summary>
    public ConfigHubRuleBuilder TenantScoped() { _builder.TenantScoped(); return this; }

    /// <summary>Adds a system activation gate independent from the user predicate.</summary>
    public ConfigHubRuleBuilder WithActivationGate(Func<IConfigurationAccessor, bool> gate) { _builder.WithActivationGate(gate); return this; }

    /// <summary>Sets a diagnostic rule name without changing its remote class key.</summary>
    public ConfigHubRuleBuilder Named(string name) { _builder.Named(name); return this; }

    /// <summary>Builds the typed class-object rule.</summary>
    public ConfigRule Build() => _builder.Build();

    /// <summary>Builds the completed fluent rule.</summary>
    public static implicit operator ConfigRule(ConfigHubRuleBuilder builder) => builder.Build();
}
