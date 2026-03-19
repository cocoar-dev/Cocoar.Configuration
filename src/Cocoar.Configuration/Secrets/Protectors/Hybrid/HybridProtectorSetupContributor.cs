using Cocoar.Capabilities;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Secrets.Core;
using Cocoar.Configuration.X509Encryption;

namespace Cocoar.Configuration.Secrets.Protectors.Hybrid;

/// <summary>
/// Setup contributor for X509 hybrid protectors.
/// Handles registration of certificate-based encryption/decryption during secrets initialization.
/// </summary>
internal sealed class HybridProtectorSetupContributor : ISecretsSetupContributor
{
    public void Apply(ConfigManagerCapabilityScope scope, IComposition composition)
    {
        var registrar = new HybridProtectorConfigurator(scope);
        composition.UsingLast<CertificateProtectorConfig>(registrar.ApplyCertificateProtector);
    }
}
