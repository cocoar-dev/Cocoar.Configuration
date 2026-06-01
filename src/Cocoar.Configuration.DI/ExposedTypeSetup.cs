using Cocoar.Capabilities;
using Cocoar.Configuration.Configure;
using Cocoar.Configuration.Core;

namespace Cocoar.Configuration.DI;

public sealed record ExposedTypePrimary<T>(Type Concrete) : IPrimaryTypeCapability
{
    public Type SelectedType => Concrete;
}

public sealed class ExposedTypeSetup<T> : SetupDefinition where T : class
{
    internal ExposedTypeSetup(ConfigManagerCapabilityScope capabilityScope): base(capabilityScope)
    {
        capabilityScope.Compose(this).WithPrimary(
            new ExposedTypePrimary<SetupDefinition>(typeof(T)));
    }

    internal override SetupDefinition Build()
    {
        GetComposer(this).Build();
        return this;
    }
}

public static class SetupBuilderExtensions
{
    public static ExposedTypeSetup<T> ExposedType<T>(this SetupBuilder builder) where T : class
        => new(SetupBuilder.GetCapabilityScopeFor(builder));
}
