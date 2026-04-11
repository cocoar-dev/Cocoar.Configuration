using Cocoar.Configuration.LocalStorage;
using Cocoar.Configuration.Providers.Abstractions;

namespace Cocoar.Configuration.Providers;

public sealed class LocalStorageProviderOptions(LocalStorageStore store)
    : IProviderConfiguration, IProviderServiceRegistration
{
    public LocalStorageStore Store { get; } = store ?? throw new ArgumentNullException(nameof(store));

    /// <summary>
    /// Returns null to indicate non-reusable. Each LocalStorage rule gets its own provider instance
    /// because each is backed by a unique <see cref="LocalStorageStore"/> tied to a specific configuration type.
    /// </summary>
    public string? GenerateProviderKey() => null;

    public IEnumerable<(Type ServiceType, object Implementation)> GetServiceRegistrations(Type concreteType)
    {
        var interfaceType = typeof(ILocalStorage<>).MakeGenericType(concreteType);
        var implType = typeof(LocalStorageAdapter<>).MakeGenericType(concreteType);
        yield return (interfaceType, Activator.CreateInstance(implType, Store)!);
    }
}
