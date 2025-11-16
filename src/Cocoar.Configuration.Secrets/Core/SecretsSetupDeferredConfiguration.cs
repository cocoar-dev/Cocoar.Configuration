using Cocoar.Capabilities;
using Cocoar.Configuration.Configure;
using Cocoar.Configuration.Core;

namespace Cocoar.Configuration.Secrets.Core;

public sealed class SecretsSetupDeferredConfiguration : IDeferredConfiguration
{
    private readonly ConfigManagerCapabilityScope _capabilityScope;

    internal SecretsSetupDeferredConfiguration(ConfigManagerCapabilityScope capabilityScope)
    {
        _capabilityScope = capabilityScope ?? throw new ArgumentNullException(nameof(capabilityScope));
    }

    public void Apply()
    {
        var composition = _capabilityScope.Owner.GetRequiredComposition();

        // Apply all registered setup contributors (AutoProtect, HybridProtector, etc.)
        composition.UsingEach<ISecretsSetupContributor>(contributor => 
            contributor.Apply(_capabilityScope, composition));
    }

}
