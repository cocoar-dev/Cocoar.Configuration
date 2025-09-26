using Cocoar.Configuration.Providers.Abstractions;
using Cocoar.Configuration.Rules;

namespace Cocoar.Configuration.Fluent;

public sealed class ProviderRuleBuilder<TProvider, TInstanceOptions, TQueryOptions>(
    Func<IConfigurationAccessor, TInstanceOptions> instanceFactory,
    Func<IConfigurationAccessor, TQueryOptions> queryFactory)
    : RuleBuilderBase<ProviderRuleBuilder<TProvider, TInstanceOptions, TQueryOptions>>, IConfigRuleBuilder
    where TProvider : ConfigurationProvider<TInstanceOptions, TQueryOptions>
    where TInstanceOptions : IProviderConfiguration
    where TQueryOptions : IProviderQuery
{
    private readonly Func<IConfigurationAccessor, TInstanceOptions> _instanceFactory = instanceFactory ?? throw new ArgumentNullException(nameof(instanceFactory));
    private readonly Func<IConfigurationAccessor, TQueryOptions> _queryFactory = queryFactory ?? throw new ArgumentNullException(nameof(queryFactory));

    public ConfigRule Build()
    {
        var registration = BuildRegistration();
        var opts = new ConfigRuleOptions(
                Required: IsRequired,
                UseWhen: UseWhen)
            .WithMount(MountPath)
            .WithSelect(SelectPath);

        return ConfigRule.Create<TProvider, TInstanceOptions, TQueryOptions>(
            _instanceFactory,
            _queryFactory,
            registration,
            opts);
    }

    public static implicit operator ConfigRule(ProviderRuleBuilder<TProvider, TInstanceOptions, TQueryOptions> builder)
    {
       return builder.Build();
    }
}
