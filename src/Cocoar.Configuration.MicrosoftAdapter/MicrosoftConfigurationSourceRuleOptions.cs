using Microsoft.Extensions.Configuration;

namespace Cocoar.Configuration.MicrosoftAdapter;

// Combined options for the Microsoft IConfigurationSource adapter (instance + query)
public sealed class MicrosoftConfigurationSourceRuleOptions
{
    public IConfigurationSource Source { get; }
    public string? BasePath { get; }
    public string? Identity { get; }
    public string? ConfigurationPrefix { get; }

    public MicrosoftConfigurationSourceRuleOptions(
        IConfigurationSource source,
        string? basePath = null,
        string? identity = null,
    string? configurationPrefix = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        BasePath = basePath;
        Identity = identity;
    ConfigurationPrefix = configurationPrefix;
    }

    public MicrosoftConfigurationSourceProviderOptions ToProviderOptions()
        => new(Source, BasePath, Identity);

    public MicrosoftConfigurationSourceProviderQueryOptions ToQueryOptions()
        => new(ConfigurationPrefix);
}
