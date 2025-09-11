using Cocoar.Configuration.Providers.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Cocoar.Configuration.MicrosoftAdapter;

public sealed class MicrosoftConfigurationSourceProviderOptions : IProviderConfiguration
{
    public IConfigurationSource Source { get; }
    public string? BasePath { get; }
    public string? Identity { get; }

    public MicrosoftConfigurationSourceProviderOptions(IConfigurationSource source, string? basePath = null, string? identity = null)
    {
        Source = source;
        BasePath = basePath;
        Identity = identity;
    }

    string IProviderConfiguration.GenerateProviderKey()
        => $"{Source.GetType().FullName}|{BasePath}|{Identity}";
}
