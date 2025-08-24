using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.MicrosoftAdapter;

public sealed class MicrosoftConfigurationSourceProviderQueryOptions(
    string? keyPrefix = null,
    string? wrapperPath = null
) : ISourceProviderQueryOptions
{
    public string? KeyPrefix { get; } = keyPrefix;
    public string? WrapperPath { get; } = wrapperPath;
}
