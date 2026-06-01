using System.Text.Json;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Extensibility;

namespace Cocoar.Configuration.Secrets.Converters;

/// <summary>
/// Capability that registers JSON converters for Secret types.
/// </summary>
internal sealed class SecretsSerializerSetup : ISerializerSetupCapability
{
    private readonly ConfigManagerCapabilityScope _scope;

    public SecretsSerializerSetup(ConfigManagerCapabilityScope scope)
    {
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
    }

    public void Configure(JsonSerializerOptions options)
    {
        options.Converters.Add(new SecretJsonConverterFactory(_scope));
    }
}
