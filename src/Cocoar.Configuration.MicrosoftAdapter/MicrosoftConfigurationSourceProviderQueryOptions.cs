using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.MicrosoftAdapter;

public sealed class MicrosoftConfigurationSourceProviderQueryOptions(
    string? configurationPrefix = null,
    string? targetPath = null
) : IProviderQuery
{
    public string? ConfigurationPrefix { get; } = configurationPrefix;
    public string? TargetPath { get; } = targetPath;
}
