using Cocoar.Configuration.Providers.HttpPollingProvider;
using Cocoar.Configuration.Fluent.ProviderOptions;

namespace Cocoar.Configuration.Fluent.Providers;

public sealed class HttpRuleBuilder : RuleBuilderBase<HttpRuleBuilder>
{
    private readonly Func<ConfigManager, HttpPollingRuleOptions> _combinedFactory;

    public HttpRuleBuilder(Func<ConfigManager, HttpPollingRuleOptions> combinedFactory)
    {
        _combinedFactory = combinedFactory ?? throw new ArgumentNullException(nameof(combinedFactory));
    }

    public ConfigRule Build()
    {
        var typeDef = BuildTypeDefinition();

        var ruleOptions = new ConfigRuleOptions { Required = _required, UseWhen = _useWhen };

        return ConfigRule.Create<HttpPollingProvider, HttpPollingProviderOptions, HttpPollingProviderQueryOptions>(
            cm => _combinedFactory(cm).ToProviderOptions(),
            cm => _combinedFactory(cm).ToQueryOptions(),
            typeDef,
            useWhen: _useWhen,
            required: _required
        );
    }
}
