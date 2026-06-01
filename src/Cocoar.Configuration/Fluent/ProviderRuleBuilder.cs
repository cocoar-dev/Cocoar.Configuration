using Cocoar.Configuration.Core;
using Cocoar.Configuration.Providers.Abstractions;
using Cocoar.Configuration.Rules;

namespace Cocoar.Configuration.Fluent;

public sealed class ProviderRuleBuilder<TProvider, TInstanceOptions, TQueryOptions> : RuleBuilderBase<ProviderRuleBuilder<TProvider, TInstanceOptions, TQueryOptions>>, IConfigRuleBuilder
    where TProvider : ConfigurationProvider<TInstanceOptions, TQueryOptions>
    where TInstanceOptions : IProviderConfiguration
    where TQueryOptions : IProviderQuery
{
    private readonly Func<IConfigurationAccessor, TInstanceOptions> _instanceFactory;
    private readonly Func<IConfigurationAccessor, TQueryOptions> _queryFactory;

    public ProviderRuleBuilder(
        Func<IConfigurationAccessor, TInstanceOptions> instanceFactory,
        Func<IConfigurationAccessor, TQueryOptions> queryFactory)
    {
        _instanceFactory = instanceFactory ?? throw new ArgumentNullException(nameof(instanceFactory));
        _queryFactory = queryFactory ?? throw new ArgumentNullException(nameof(queryFactory));
    }

    public ProviderRuleBuilder(
        Func<IConfigurationAccessor, TInstanceOptions> instanceFactory,
        Func<IConfigurationAccessor, TQueryOptions> queryFactory,
        Type concreteType)
        : this(instanceFactory, queryFactory)
    {
        ConcreteType = concreteType;
    }

    public ConfigRule Build()
    {
        var registration = BuildRegistration();
        var opts = new ConfigRuleOptions(
                Required: IsRequired,
                UseWhen: UseWhen,
                Name: Name,
                TenantScoped: IsTenantScoped,
                ActivationGate: ActivationGate)
            .WithMount(MountPath)
            .WithSelect(SelectPath);

        return ConfigRule.Create<TProvider, TInstanceOptions, TQueryOptions>(
            _instanceFactory,
            _queryFactory,
            registration,
            opts);
    }

    /// <remarks>
    /// This implicit conversion triggers <see cref="Build"/> immediately.
    /// Ensure the fluent chain is complete before the implicit conversion occurs,
    /// as any further method calls after conversion will not be reflected in the built rule.
    /// </remarks>
    public static implicit operator ConfigRule(ProviderRuleBuilder<TProvider, TInstanceOptions, TQueryOptions> builder)
    {
       return builder.Build();
    }
}
