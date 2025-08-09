using Cocoar.Configuration.Fluent;

namespace Cocoar.Configuration.HttpPolling;

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

        return ConfigRule.Create<HttpPollingProvider, HttpPollingProviderOptions, HttpPollingProviderQueryOptions>(
            cm => _combinedFactory(cm).ToProviderOptions(),
            cm => _combinedFactory(cm).ToQueryOptions(),
            typeDef,
            useWhen: _useWhen,
            required: _required
        );
    }
}
