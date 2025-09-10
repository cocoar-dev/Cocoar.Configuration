using Cocoar.Configuration.Fluent;
using Cocoar.Configuration.Providers.FileSourceProvider;

namespace Cocoar.Configuration.Providers.FileSourceProvider.Fluent;

public sealed class FileRuleBuilder : RuleBuilderBase<FileRuleBuilder>, IConfigRuleBuilder
{
    private readonly Func<ConfigManager, FileSourceRuleOptions> _combinedFactory;

    public FileRuleBuilder(Func<ConfigManager, FileSourceRuleOptions> combinedFactory)
    {
        _combinedFactory = combinedFactory ?? throw new ArgumentNullException(nameof(combinedFactory));
    }

    public ConfigRule Build()
    {
        var typeDef = BuildTypeDefinition();
        
        return ConfigRule.Create<FileSourceProvider, FileSourceProviderOptions, FileSourceProviderQueryOptions>(
            cm => _combinedFactory(cm).ToProviderOptions(),
            cm => _combinedFactory(cm).ToQueryOptions(),
            typeDef,
            useWhen: _useWhen,
            required: _required
        );
    }
}
