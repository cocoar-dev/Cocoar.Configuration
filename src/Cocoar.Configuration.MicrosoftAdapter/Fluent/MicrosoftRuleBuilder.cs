using Cocoar.Configuration.Fluent;

namespace Cocoar.Configuration.MicrosoftAdapter.Fluent;

public sealed class MicrosoftRuleBuilder : RuleBuilderBase<MicrosoftRuleBuilder>, IConfigRuleBuilder
{
    private readonly Func<ConfigManager, MicrosoftConfigurationSourceRuleOptions> _factory;

    public MicrosoftRuleBuilder(Func<ConfigManager, MicrosoftConfigurationSourceRuleOptions> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public ConfigRule Build()
    {
        var typeDef = BuildTypeDefinition();
        return ConfigRule.Create<MicrosoftConfigurationSourceProvider, MicrosoftConfigurationSourceProviderOptions, MicrosoftConfigurationSourceProviderQueryOptions>(
            cm => _factory(cm).ToProviderOptions(),
            cm => _factory(cm).ToQueryOptions(),
            typeDef,
            useWhen: _useWhen,
            required: _required
        );
    }
}
