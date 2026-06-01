using Cocoar.Capabilities;

namespace Cocoar.Configuration.Configure;

public interface IPrimaryTypeCapability: IPrimaryCapability
{
    public Type SelectedType { get; }
}

public sealed record ConcreteTypePrimary<T>(Type Concrete) : IPrimaryTypeCapability
{
    public Type SelectedType => Concrete;
}

public sealed record ExposeAsCapability<T>(Type ContractType);
