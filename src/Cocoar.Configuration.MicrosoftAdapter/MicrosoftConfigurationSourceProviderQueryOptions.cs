using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.MicrosoftAdapter;

public sealed class MicrosoftConfigurationSourceProviderQueryOptions(
    string? configurationPrefix = null
) : IProviderQuery
{
    public string? ConfigurationPrefix { get; } = configurationPrefix;
}
