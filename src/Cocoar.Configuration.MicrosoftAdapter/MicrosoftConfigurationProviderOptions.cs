using Cocoar.Configuration.Providers.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Cocoar.Configuration.MicrosoftAdapter;

/// <summary>
/// Provider options that accept an <see cref="IConfiguration"/> instance directly,
/// avoiding the need to work with low-level <see cref="IConfigurationSource"/>.
/// </summary>
public sealed class MicrosoftConfigurationProviderOptions : IProviderConfiguration
{
    public IConfiguration Configuration { get; }

    public MicrosoftConfigurationProviderOptions(IConfiguration configuration)
    {
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    // Each rule gets its own provider instance since IConfiguration identity
    // cannot be reliably hashed. Return null to prevent sharing.
    string? IProviderConfiguration.GenerateProviderKey() => null;
}
