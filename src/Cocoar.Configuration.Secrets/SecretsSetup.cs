using Cocoar.Capabilities;
using Cocoar.Configuration.Configure;
using Cocoar.Configuration.Core;
using Cocoar.Configuration.Extensibility;
using Cocoar.Configuration.Secrets.Converters;
using Cocoar.Configuration.Secrets.Core;
using Cocoar.Configuration.X509Encryption;

namespace Cocoar.Configuration.Secrets;

public sealed class SecretsBuilder : SetupDefinition
{
    private readonly Composer _composer;

    internal SecretsBuilder(ConfigManagerCapabilityScope capabilityScope)
        : base(capabilityScope)
    {
        _composer = CapabilityScope.Owner.GetRequiredComposer();
        
        if (!_composer.Has<ISerializerSetupCapability>())
        {
            _composer.AddAs<(IDeferredConfiguration, SecretsSetupDeferredConfiguration)>(
                new SecretsSetupDeferredConfiguration(CapabilityScope));
            _composer.AddAs<ISerializerSetupCapability>(new SecretsSerializerSetup(CapabilityScope));
        }
    }

    internal override SetupDefinition Build() => this;

    internal static Composer GetComposerFor(SecretsBuilder builder) => builder._composer;
}

public static class SecretsSetupExtensions
{
    public static SecretsBuilder Secrets(this SetupBuilder builder)
        => new(SetupBuilder.GetCapabilityScopeFor(builder));
}
