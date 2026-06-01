using Cocoar.Capabilities;
using Cocoar.Configuration.Core;

namespace Cocoar.Configuration.Secrets.Core;

public interface ISecretsSetupContributor
{
    void Apply(ConfigManagerCapabilityScope scope, IComposition composition);
}
