using System;
using Microsoft.Extensions.Configuration;

namespace Cocoar.Configuration.MicrosoftAdapter;

// Combined options for the Microsoft IConfigurationSource adapter (instance + query)
public sealed class MicrosoftConfigurationSourceRuleOptions
{
    public IConfigurationSource Source { get; }
    public string? BasePath { get; }
    public string? Identity { get; }
    public string? KeyPrefix { get; }
    public string? WrapperPath { get; }

    public MicrosoftConfigurationSourceRuleOptions(
        IConfigurationSource source,
        string? basePath = null,
        string? identity = null,
        string? keyPrefix = null,
        string? wrapperPath = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        BasePath = basePath;
        Identity = identity;
        KeyPrefix = keyPrefix;
        WrapperPath = wrapperPath;
    }

    public MicrosoftConfigurationSourceProviderOptions ToProviderOptions()
        => new(Source, BasePath, Identity);

    public MicrosoftConfigurationSourceProviderQueryOptions ToQueryOptions()
        => new(KeyPrefix, WrapperPath);
}
