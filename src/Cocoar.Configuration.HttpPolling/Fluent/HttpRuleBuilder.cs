using Cocoar.Configuration.Fluent;

namespace Cocoar.Configuration.HttpPolling.Fluent;

public sealed class HttpRuleBuilder : RuleBuilderBase<HttpRuleBuilder>, IConfigRuleBuilder
{
    private readonly Func<ConfigManager, Cocoar.Configuration.HttpPolling.Fluent.ProviderOptions.HttpPollingRuleOptions> _combinedFactory;

    public HttpRuleBuilder(Func<ConfigManager, Cocoar.Configuration.HttpPolling.Fluent.ProviderOptions.HttpPollingRuleOptions> combinedFactory)
    {
        _combinedFactory = combinedFactory ?? throw new ArgumentNullException(nameof(combinedFactory));
    }

    public ConfigRule Build()
    {
        var typeDef = BuildTypeDefinition();

        return ConfigRule.Create<
            global::Cocoar.Configuration.Providers.HttpPollingProvider.HttpPollingProvider,
            global::Cocoar.Configuration.Providers.HttpPollingProvider.HttpPollingProviderOptions,
            global::Cocoar.Configuration.Providers.HttpPollingProvider.HttpPollingProviderQueryOptions
        >(
            cm =>
            {
                var ro = _combinedFactory(cm);
                return new global::Cocoar.Configuration.Providers.HttpPollingProvider.HttpPollingProviderOptions(
                    ro.BaseAddress,
                    ro.PollInterval,
                    ro.Handler
                );
            },
            cm =>
            {
                var ro = _combinedFactory(cm);
                return new global::Cocoar.Configuration.Providers.HttpPollingProvider.HttpPollingProviderQueryOptions(
                    ro.UrlPathOrAbsolute,
                    ro.MemberPath,
                    ro.MemberWrapper,
                    ro.Headers
                );
            },
            typeDef,
            useWhen: _useWhen,
            required: _required
        );
    }
}
