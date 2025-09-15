using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Fluent;

// Generic builder to avoid per-provider bespoke builders.
public sealed class ProviderRuleBuilder<TProvider, TInstanceOptions, TQueryOptions> : RuleBuilderBase<ProviderRuleBuilder<TProvider, TInstanceOptions, TQueryOptions>>, IConfigRuleBuilder
    where TProvider : ConfigurationProvider<TInstanceOptions, TQueryOptions>
    where TInstanceOptions : IProviderConfiguration
    where TQueryOptions : IProviderQuery
{
    private readonly Func<ConfigManager, TInstanceOptions> _instanceFactory;
    private readonly Func<ConfigManager, TQueryOptions> _queryFactory;

    public ProviderRuleBuilder(
        Func<ConfigManager, TInstanceOptions> instanceFactory,
        Func<ConfigManager, TQueryOptions> queryFactory)
    {
        _instanceFactory = instanceFactory ?? throw new ArgumentNullException(nameof(instanceFactory));
        _queryFactory = queryFactory ?? throw new ArgumentNullException(nameof(queryFactory));
    }

    public IEnumerable<ConfigRule> BuildRules()
    {
        var typeDefs = BuildTypeDefinitions();
        foreach (var typeDef in typeDefs)
        {
            var opts = new ConfigRuleOptions(
                    Required: _required,
                    UseWhen: _useWhen)
                .WithMount(_mountPath);

            var rule = ConfigRule.Create<TProvider, TInstanceOptions, TQueryOptions>(
                _instanceFactory,
                _queryFactory,
                typeDef,
                opts);
            yield return rule;
        }
    }

    public ConfigRule Build()
    {
        var rules = BuildRules().ToList();
        if (rules.Count != 1)
            throw new InvalidOperationException($"Build() can only be used when exactly one registration is configured, but found {rules.Count}. Use BuildRules() instead.");

        return rules[0];
    }
}
