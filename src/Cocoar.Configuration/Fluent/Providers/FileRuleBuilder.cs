using Cocoar.Configuration.Providers.FileSourceProvider;
using Cocoar.Configuration.Fluent.ProviderOptions;

namespace Cocoar.Configuration.Fluent.Providers;

public sealed class FileRuleBuilder : RuleBuilderBase<FileRuleBuilder>
{
    private readonly Func<ConfigManager, FileSourceRuleOptions> _combinedFactory;

    public FileRuleBuilder(Func<ConfigManager, FileSourceRuleOptions> combinedFactory)
    {
        _combinedFactory = combinedFactory ?? throw new ArgumentNullException(nameof(combinedFactory));
    }

    public ConfigRule Build()
    {
        var typeDef = BuildTypeDefinition();
        var ruleOptions = new ConfigRuleOptions { Required = _required, UseWhen = _useWhen };

        return ConfigRule.Create<FileSourceProvider, FileSourceProviderOptions, FileSourceProviderQueryOptions>(
            cm => _combinedFactory(cm).ToProviderOptions(),
            cm => _combinedFactory(cm).ToQueryOptions(),
            typeDef,
            useWhen: _useWhen,
            required: _required
        );
    }
}
