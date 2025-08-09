using Cocoar.Configuration.Providers.EnvironmentVariableProvider;
using Cocoar.Configuration.Fluent.ProviderOptions;

namespace Cocoar.Configuration.Fluent.Providers;

public sealed class EnvironmentRuleBuilder : RuleBuilderBase<EnvironmentRuleBuilder>
{
    private readonly Func<ConfigManager, EnvironmentVariableRuleOptions> _combinedFactory;

    public EnvironmentRuleBuilder(Func<ConfigManager, EnvironmentVariableRuleOptions> combinedFactory)
    {
        _combinedFactory = combinedFactory ?? throw new ArgumentNullException(nameof(combinedFactory));
    }

    public ConfigRule Build()
    {
        var typeDef = BuildTypeDefinition();
        var ruleOptions = new ConfigRuleOptions { Required = _required, UseWhen = _useWhen };

        return ConfigRule.Create<EnvironmentVariableProvider, EnvironmentVariableProviderOptions, EnvironmentVariableProviderQueryOptions>(
            cm => _combinedFactory(cm).ToProviderOptions(),
            cm => _combinedFactory(cm).ToQueryOptions(),
            typeDef,
            useWhen: _useWhen,
            required: _required
        );
    }
}
